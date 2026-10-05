var PurrRelayPingLibrary = {
    $PurrRelayPing: {
        nextId: 1,
        jobs: {},

        timedFetch: function (url) {
            var start = performance.now();
            return fetch(url, { cache: 'no-store', mode: 'cors', credentials: 'omit' }).then(function (response) {
                return response.text().then(function () {
                    if (!response.ok) throw new Error('HTTP ' + response.status);
                    return performance.now() - start;
                });
            });
        },

        measure: function (url) {
            return PurrRelayPing.timedFetch(url).then(function () {
                return Promise.all([PurrRelayPing.timedFetch(url), PurrRelayPing.timedFetch(url)]);
            }).then(function (times) {
                return { ms: Math.min(times[0], times[1]), error: '' };
            }).catch(function (error) {
                return { ms: -1, error: String(error && error.message ? error.message : error) };
            });
        },

        start: function (urls) {
            var id = PurrRelayPing.nextId++;
            var job = { done: false, result: null };
            PurrRelayPing.jobs[id] = job;
            Promise.all(urls.map(PurrRelayPing.measure)).then(function (results) {
                job.result = JSON.stringify({ results: results });
                job.done = true;
            });
            return id;
        }
    },

    PurrRelayPing_Start__deps: ['$PurrRelayPing', '$UTF8ToString'],
    PurrRelayPing_Start: function (urlsJsonPtr) {
        var urls;
        try { urls = JSON.parse(UTF8ToString(urlsJsonPtr)).urls || []; } catch (_) { urls = []; }
        return PurrRelayPing.start(urls);
    },

    PurrRelayPing_Poll__deps: ['$PurrRelayPing', '$stringToNewUTF8'],
    PurrRelayPing_Poll: function (id) {
        var job = PurrRelayPing.jobs[id];
        if (!job || !job.done) return 0;
        delete PurrRelayPing.jobs[id];
        return stringToNewUTF8(job.result);
    },

    PurrRelayPing_Free__deps: ['free'],
    PurrRelayPing_Free: function (ptr) {
        if (ptr) _free(ptr);
    }
};

autoAddDeps(PurrRelayPingLibrary, '$PurrRelayPing');
mergeInto(LibraryManager.library, PurrRelayPingLibrary);
