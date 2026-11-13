// Browser side of RecordingFile (see WebRecording.cs). C# runs on the page's main thread in WebGL, so the work a
// desktop build does on its writer thread happens in a Web Worker: compressing records (CompressionStream), storing
// each recording in IndexedDB as it is written, keeping the store under its size cap, and uploading finished
// recordings to the recordings service. The main thread only copies each record out of the WebAssembly heap.
//
// The worker is plain ES5 built from a function's source, so it needs no extra file next to the build and survives
// Emscripten's minifier (it references nothing outside itself).
var PurrNetRecordingLib = {
    $PurrNetRec: {
        worker: null,
        failed: false,
        next: 1,
        // Per recording: [bytes stored, worker ms compressing, longest compression ms, deflate available].
        stats: {},

        start: function () {
            if (PurrNetRec.worker || PurrNetRec.failed)
                return PurrNetRec.worker;
            try {
                var source = '(' + PurrNetRec.main.toString() + ')();';
                var url = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
                var worker = new Worker(url);
                worker.onmessage = function (e) {
                    var m = e.data;
                    if (m.t === 'stat') {
                        var s = PurrNetRec.stats[m.h];
                        if (s) {
                            s[0] += m.bytes;
                            s[1] += m.ms;
                            if (m.ms > s[2]) s[2] = m.ms;
                            s[3] = m.deflate ? 1 : 0;
                        }
                    } else if (m.t === 'warn') {
                        console.warn('[Recorder] ' + m.text);
                    }
                };
                worker.onerror = function (e) {
                    console.warn('[Recorder] Recording worker failed: ' + (e && e.message ? e.message : e));
                };
                PurrNetRec.worker = worker;
            } catch (e) {
                // No workers, or a page policy that forbids blob: workers.
                PurrNetRec.failed = true;
            }
            return PurrNetRec.worker;
        },

        // Runs inside the worker.
        main: function () {
            var STORE = 'PurrDiction.Recordings';
            var LOCK = 'purrrec:';
            var RETRY_MS = 60000, HELD_GAP_MS = 5000, TICKET_MS = 20000, FRESH_MS = 10000, STALE_MS = 120000;
            // Files the service said "not now" to, and when each may be tried again: the others are sent meanwhile.
            var held = {};
            // The file being sent, or whose sending was cut off: it goes first next time and is never trimmed.
            var sending = null;
            // Files the service has answered for whose removal from the store failed: never sent again.
            var done = {};
            var locks = self.navigator && self.navigator.locks ? self.navigator.locks : null;
            var deflateOk = (function () {
                try { new CompressionStream('deflate-raw'); return true; } catch (e) { return false; }
            })();

            var db = null;
            var memory = {}; // used when IndexedDB cannot be opened: name -> { meta, parts }
            var open = {};   // handle -> { meta, seq, failed, release }
            var active = {}; // names this worker is writing
            var chain = Promise.resolve();
            var uploads = null, idle = false, uploading = false, blocked = false, retryAt = 0, timer = 0, abort = null;

            function warn(text) { self.postMessage({ t: 'warn', text: text }); }

            function request(r) {
                return new Promise(function (resolve, reject) {
                    r.onsuccess = function () { resolve(r.result); };
                    r.onerror = function () { reject(r.error); };
                });
            }

            function done(t) {
                return new Promise(function (resolve, reject) {
                    t.oncomplete = function () { resolve(); };
                    t.onerror = t.onabort = function () { reject(t.error); };
                });
            }

            var ready = new Promise(function (resolve) {
                try {
                    var r = indexedDB.open(STORE, 1);
                    r.onupgradeneeded = function () {
                        r.result.createObjectStore('files', { keyPath: 'name' });
                        r.result.createObjectStore('parts');
                    };
                    r.onsuccess = function () { db = r.result; resolve(); };
                    r.onerror = function () { resolve(); };
                } catch (e) {
                    resolve();
                }
            });

            function range(name) { return IDBKeyRange.bound([name, 0], [name, Infinity]); }

            function saveMeta(meta) {
                if (!db) {
                    (memory[meta.name] = memory[meta.name] || { parts: [] }).meta = meta;
                    return Promise.resolve();
                }
                var t = db.transaction('files', 'readwrite');
                t.objectStore('files').put(meta);
                return done(t);
            }

            function savePart(meta, seq, bytes) {
                meta.size += bytes.length;
                meta.touched = Date.now();
                if (!db) {
                    var m = memory[meta.name];
                    m.parts.push(bytes);
                    m.meta = meta;
                    return Promise.resolve();
                }
                var t = db.transaction(['files', 'parts'], 'readwrite');
                t.objectStore('parts').put(bytes, [meta.name, seq]);
                t.objectStore('files').put(meta);
                return done(t);
            }

            function listFiles() {
                if (!db)
                    return Promise.resolve(Object.keys(memory).map(function (k) { return memory[k].meta; }));
                return request(db.transaction('files', 'readonly').objectStore('files').getAll());
            }

            function readParts(name) {
                if (!db)
                    return Promise.resolve(memory[name] ? memory[name].parts : []);
                return request(db.transaction('parts', 'readonly').objectStore('parts').getAll(range(name)));
            }

            function removeFile(name) {
                if (!db) {
                    delete memory[name];
                    return Promise.resolve();
                }
                var t = db.transaction(['files', 'parts'], 'readwrite');
                t.objectStore('files').delete(name);
                t.objectStore('parts').delete(range(name));
                return done(t);
            }

            // Names some tab is still writing: held locks, or (without the Locks API) recently written.
            function writing() {
                var names = {};
                for (var k in active) names[k] = true;
                if (!locks)
                    return Promise.resolve(names);
                return locks.query().then(function (state) {
                    (state.held || []).forEach(function (l) {
                        if (l.name.indexOf(LOCK) === 0) names[l.name.substring(LOCK.length)] = true;
                    });
                    return names;
                }, function () { return names; });
            }

            function finished(meta, names) {
                if (names[meta.name]) return false;
                if (meta.state === 'done') return true;
                // Left open by a tab that closed mid-match: the records written so far are a valid recording.
                var age = Date.now() - meta.touched;
                return age > (locks ? FRESH_MS : STALE_MS);
            }

            function hold(name) {
                if (!locks) return null;
                var release;
                var held = new Promise(function (resolve) { release = resolve; });
                locks.request(LOCK + name, function () { return held; }).catch(function () {});
                return release;
            }

            function deflate(bytes) {
                var cs = new CompressionStream('deflate-raw');
                var w = cs.writable.getWriter();
                w.write(bytes).catch(function () {});
                w.close().catch(function () {});
                return new Response(cs.readable).arrayBuffer().then(function (b) { return new Uint8Array(b); });
            }

            // kind byte (0x80: deflated), LEB128 length, payload.
            function frame(kind, payload) {
                var head = [kind], n = payload.length;
                while (n >= 0x80) {
                    head.push((n % 0x80) | 0x80);
                    n = Math.floor(n / 0x80);
                }
                head.push(n);
                var out = new Uint8Array(head.length + payload.length);
                out.set(head, 0);
                out.set(payload, head.length);
                return out;
            }

            // Deletes the oldest finished recordings until the store fits its cap: never the one being sent, and a
            // client's parts (tiny next to a recording) only when deleting every other file was not enough.
            function trim(max) {
                if (!(max > 0)) return Promise.resolve();
                return Promise.all([listFiles(), writing()]).then(function (r) {
                    var files = r[0], names = r[1], total = 0, work = Promise.resolve();
                    files.forEach(function (f) { total += f.size; });
                    var old = files.filter(function (f) { return finished(f, names) && f.name !== sending; })
                        .sort(function (a, b) { return a.created - b.created; });
                    [false, true].forEach(function (parts) {
                        old.forEach(function (f) {
                            if (total <= max || (!!(f.link && f.link.kind === 'part')) !== parts) return;
                            total -= f.size;
                            work = work.then(function () { return removeFile(f.name); });
                        });
                    });
                    return work;
                });
            }

            function handle(m) {
                if (m.t === 'open') {
                    return ready.then(function () {
                        var now = Date.now();
                        var meta = { name: m.name, size: 0, state: 'open', created: now, touched: now };
                        active[m.name] = true;
                        open[m.h] = { meta: meta, seq: 0, failed: false, max: m.max, release: hold(m.name) };
                        return removeFile(m.name).then(function () { return saveMeta(meta); });
                    });
                }
                if (m.t === 'link') {
                    // What the upload request says about this file besides its name and size (recording id, part...).
                    var l = open[m.h];
                    if (!l) return;
                    try { l.meta.link = JSON.parse(m.json); } catch (e) { return; }
                    return saveMeta(l.meta);
                }
                if (m.t === 'append') {
                    var f = open[m.h];
                    if (!f || f.failed) return;
                    var started = performance.now();
                    var compress = m.kind >= 0 && m.compress && deflateOk;
                    var payload = compress ? deflate(m.bytes).catch(function () { compress = false; return m.bytes; }) : Promise.resolve(m.bytes);
                    return payload.then(function (p) {
                        var ms = compress ? performance.now() - started : 0;
                        var bytes = m.kind < 0 ? p : frame(compress ? (m.kind | 0x80) : m.kind, p);
                        self.postMessage({ t: 'stat', h: m.h, bytes: bytes.length, ms: ms, deflate: deflateOk });
                        return savePart(f.meta, f.seq++, bytes).catch(function (e) {
                            // Usually the browser's storage quota: keep what was stored, drop the rest.
                            f.failed = true;
                            warn('Storing ' + f.meta.name + ' failed: ' + e);
                        });
                    });
                }
                if (m.t === 'close') {
                    var c = open[m.h];
                    delete open[m.h];
                    if (!c) return;
                    c.meta.state = 'done';
                    return saveMeta(c.meta).then(function () {
                        delete active[c.meta.name];
                        if (c.release) c.release();
                        return trim(c.max);
                    }).then(function () {
                        retryAt = 0;
                        schedule();
                    });
                }
                if (m.t === 'uploads') {
                    uploads = { endpoint: m.endpoint.replace(/\/+$/, ''), key: m.key, max: m.max };
                    return ready.then(function () { return trim(m.max); }).then(function () {
                        retryAt = 0;
                        schedule();
                    });
                }
            }

            // ---- Uploads: the same protocol and rules as RecordingUploader -------------------------------

            function schedule() {
                if (timer) clearTimeout(timer);
                timer = 0;
                if (!uploads || blocked || !idle || uploading) return;
                timer = setTimeout(send, Math.max(0, retryAt - Date.now()));
            }

            function later() { retryAt = Date.now() + RETRY_MS; }

            function post(url, body) {
                var a = abort = new AbortController();
                var timeout = setTimeout(function () { a.abort(); }, TICKET_MS);
                return fetch(url, {
                    method: 'POST', body: body, signal: a.signal,
                    headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + uploads.key }
                }).then(function (r) { clearTimeout(timeout); return r; }, function (e) { clearTimeout(timeout); throw e; });
            }

            function refused(r, name) {
                sending = null;
                return r.text().then(function (text) {
                    // Wrong key or the service is off for this project: stop until the next launch. A quota is "not now".
                    var quota = text && text.indexOf('quota') >= 0;
                    if (r.status === 401 || (r.status === 403 && !quota)) {
                        if (!blocked) warn('Uploads refused (' + r.status + '): ' + text);
                        blocked = true;
                    } else {
                        // "Not now" for this file: it waits while the next one is tried after a short pause.
                        held[name] = Date.now() + RETRY_MS;
                        retryAt = Date.now() + HELD_GAP_MS;
                    }
                });
            }

            function sendOne(meta) {
                // Cleared when the service has answered for this file; a sending that was cut off keeps it.
                sending = meta.name;
                var sent = function () {
                    sending = null;
                    done[meta.name] = true;
                    return removeFile(meta.name).then(function () { delete done[meta.name]; });
                };
                return readParts(meta.name).then(function (parts) {
                    var body = new Blob(parts);
                    var ticket = { fileName: meta.name, size: body.size };
                    for (var k in (meta.link || {})) ticket[k] = meta.link[k];
                    return post(uploads.endpoint + '/uploads', JSON.stringify(ticket)).then(function (r) {
                        if (r.status !== 200 && r.status !== 201) return refused(r, meta.name);
                        return r.json().then(function (ticket) {
                            // Not wanted (sampling, a cap, a rejected file): sending it later would get the same answer.
                            if (ticket.skip || !ticket.url) return sent();
                            var a = abort = new AbortController();
                            return fetch(ticket.url, {
                                method: 'PUT', body: body, signal: a.signal,
                                headers: { 'Content-Type': ticket.contentType || 'application/octet-stream' }
                            }).then(function (put) {
                                if (!put.ok) return later();
                                return post(uploads.endpoint + '/uploads/' + encodeURIComponent(ticket.id) + '/complete', '{}').then(function (c) {
                                    return c.status === 200 ? sent() : refused(c, meta.name);
                                });
                            });
                        });
                    });
                });
            }

            function sendOldest() {
                return Promise.all([listFiles(), writing()]).then(function (r) {
                    var now = Date.now(), due = Infinity;
                    var files = r[0].filter(function (f) { return finished(f, r[1]); })
                        .filter(function (f) {
                            // The service has it; all that is left is to remove it.
                            if (done[f.name]) { removeFile(f.name).then(function () { delete done[f.name]; }, function () {}); return false; }
                            if (!(held[f.name] > now)) return true;
                            due = Math.min(due, held[f.name]);
                            return false;
                        })
                        // The newest first: the trim deletes from the other end.
                        .sort(function (a, b) { return b.created - a.created; });
                    var resumed = files.filter(function (f) { return f.name === sending; });
                    if (resumed.length) files = resumed;
                    // Nothing to send, or only held files: back when the first of them is due.
                    if (files.length === 0) { retryAt = Math.min(due, now + RETRY_MS); return; }
                    return sendOne(files[0]);
                });
            }

            function send() {
                timer = 0;
                if (!uploads || blocked || !idle || uploading) return;
                uploading = true;
                // One upload at a time across every tab of this game.
                var run = locks
                    ? locks.request(LOCK + 'upload', { ifAvailable: true }, function (lock) { return lock ? sendOldest() : later(); })
                    : sendOldest();
                run.catch(function () { later(); }).then(function () {
                    uploading = false;
                    abort = null;
                    schedule();
                });
            }

            self.onmessage = function (e) {
                var m = e.data;
                if (m.t === 'idle') {
                    idle = m.idle;
                    // A session started: get off the network, the file is sent again later.
                    if (!idle && abort) abort.abort();
                    schedule();
                    return;
                }
                chain = chain.then(function () { return ready.then(function () { return handle(m); }); })
                    .catch(function (err) { warn('' + (err && err.message ? err.message : err)); });
            };
        }
    },

    PurrNetRec_Open__deps: ['$PurrNetRec'],
    PurrNetRec_Open: function (namePtr, maxLocalBytes) {
        var worker = PurrNetRec.start();
        if (!worker) return 0;
        var h = PurrNetRec.next++;
        PurrNetRec.stats[h] = [0, 0, 0, 1];
        worker.postMessage({ t: 'open', h: h, name: UTF8ToString(namePtr), max: maxLocalBytes });
        return h;
    },

    PurrNetRec_Append__deps: ['$PurrNetRec'],
    PurrNetRec_Append: function (h, kind, compress, ptr, length) {
        var worker = PurrNetRec.worker;
        if (!worker) return;
        // A copy the worker takes over, so the WebAssembly buffer can be reused right away.
        var bytes = HEAPU8.slice(ptr, ptr + length);
        worker.postMessage({ t: 'append', h: h, kind: kind, compress: compress !== 0, bytes: bytes }, [bytes.buffer]);
    },

    PurrNetRec_Link__deps: ['$PurrNetRec'],
    PurrNetRec_Link: function (h, jsonPtr) {
        if (PurrNetRec.worker) PurrNetRec.worker.postMessage({ t: 'link', h: h, json: UTF8ToString(jsonPtr) });
    },

    PurrNetRec_Close__deps: ['$PurrNetRec'],
    PurrNetRec_Close: function (h) {
        if (PurrNetRec.worker) PurrNetRec.worker.postMessage({ t: 'close', h: h });
    },

    PurrNetRec_Stat__deps: ['$PurrNetRec'],
    PurrNetRec_Stat: function (h, which) {
        var s = PurrNetRec.stats[h];
        return s ? s[which] : 0;
    },

    PurrNetRec_Uploads__deps: ['$PurrNetRec'],
    PurrNetRec_Uploads: function (endpointPtr, keyPtr, maxLocalBytes) {
        var worker = PurrNetRec.start();
        if (worker)
            worker.postMessage({ t: 'uploads', endpoint: UTF8ToString(endpointPtr), key: UTF8ToString(keyPtr), max: maxLocalBytes });
    },

    PurrNetRec_SetIdle__deps: ['$PurrNetRec'],
    PurrNetRec_SetIdle: function (idle) {
        if (PurrNetRec.worker) PurrNetRec.worker.postMessage({ t: 'idle', idle: idle !== 0 });
    }
};

mergeInto(LibraryManager.library, PurrNetRecordingLib);
