using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PurrNet.Utils;
using Unity.Collections.LowLevel.Unsafe;

namespace PurrNet.Recording
{
    internal static class RecordingObjectId
    {
        // Keep the full Unity ID in caches and recordings; newer EntityIds cannot be cast to int.
        public static long Of(UnityEngine.Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return unchecked((long)UnityEngine.EntityId.ToULong(PurrObjectId.Of(obj)));
#else
            return PurrObjectId.Of(obj);
#endif
        }
    }

    /// <summary>
    /// Fixed-size row description of a struct: its unmanaged leaves packed without padding, plus the
    /// fields that need a tagged encoding. Built once per type by reflection.
    /// </summary>
    public sealed class RowLayout
    {
        public struct Column
        {
            public string name;
            public string type;
            public int offset;
            public int size;
            public Type enumType;
        }

        public struct Segment
        {
            public int source;
            public int target;
            public int size;
        }

        public sealed class ManagedField
        {
            public string name;
            public FieldInfo[] path;
        }

        public int id;
        public Type type;
        public int rowSize;
        public readonly List<Column> columns = new List<Column>();
        public readonly List<Segment> segments = new List<Segment>();
        public readonly List<ManagedField> managed = new List<ManagedField>();

        public bool hasManaged => managed.Count > 0;
        /// <summary>In-place writers of <see cref="managed"/>, null entries where reflection is used.</summary>
        internal ManagedFieldWriter[] writers;

        /// <summary>
        /// Copies the unmanaged leaves of <paramref name="value"/> into <paramref name="row"/> at its end.
        /// </summary>
        public unsafe void WriteFixed<T>(ref T value, RecordingBuffer row) where T : struct
        {
            row.Ensure(rowSize);
            byte* src = (byte*)UnsafeUtility.AddressOf(ref value);
            fixed (byte* dst = &row.data[row.length])
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    var s = segments[i];
                    UnsafeUtility.MemCpy(dst + s.target, src + s.source, s.size);
                }
            }
            row.length += rowSize;
        }

        public static object ReadManaged(object boxed, ManagedField field)
        {
            object current = boxed;
            for (int i = 0; i < field.path.Length && current != null; i++)
                current = field.path[i].GetValue(current);
            return current;
        }
    }

    /// <summary>
    /// Field list of a class or struct written as a tagged object value.
    /// </summary>
    public sealed class ObjectLayout
    {
        public int id;
        public Type type;
        public FieldInfo[] fields;
        public string[] names;
    }

    /// <summary>
    /// Assigns type ids and collects the TYPE records that still have to be written.
    /// </summary>
    public sealed class RecordingTypeRegistry
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const int MaxDepth = 8;

        readonly Dictionary<Type, RowLayout> _rows = new Dictionary<Type, RowLayout>();
        readonly Dictionary<Type, ObjectLayout> _objects = new Dictionary<Type, ObjectLayout>();
        readonly List<string> _pending = new List<string>();
        int _nextId = 1;

        /// <summary>TYPE record payloads registered since the last call.</summary>
        public bool TakePending(List<string> into)
        {
            if (_pending.Count == 0)
                return false;
            into.AddRange(_pending);
            _pending.Clear();
            return true;
        }

        public RowLayout GetRow(Type type)
        {
            if (_rows.TryGetValue(type, out var layout))
                return layout;

            layout = new RowLayout { id = _nextId++, type = type };
            Flatten(layout, type, "", 0, new List<FieldInfo>(), 0);
            MergeSegments(layout);
            _rows[type] = layout;
            _pending.Add(DescribeRow(layout));
            return layout;
        }

        public ObjectLayout GetObject(Type type)
        {
            if (_objects.TryGetValue(type, out var layout))
                return layout;

            var fields = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            {
                foreach (var f in t.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType.IsPointer)
                        continue;
                    fields.Add(f);
                }
            }

            layout = new ObjectLayout
            {
                id = _nextId++,
                type = type,
                fields = fields.ToArray(),
                names = fields.ConvertAll(f => CleanName(f.Name)).ToArray()
            };
            _objects[type] = layout;

            var json = new JsonBuilder().BeginObject()
                .Prop("id", layout.id).Prop("kind", "object").Prop("name", TypeName(type))
                .Key("fields").BeginArray();
            foreach (var n in layout.names)
                json.Value(n);
            json.EndArray().EndObject();
            _pending.Add(json.ToString());
            return layout;
        }

        void Flatten(RowLayout layout, Type type, string prefix, int baseOffset, List<FieldInfo> path, int depth)
        {
            foreach (var field in type.GetFields(InstanceFields))
            {
                var ft = field.FieldType;
                if (ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr))
                    continue;

                string name = prefix + CleanName(field.Name);
                int offset = baseOffset + UnsafeUtility.GetFieldOffset(field);
                path.Add(field);

                if (ft.IsEnum && TryScalar(Enum.GetUnderlyingType(ft), out var scalar, out int size))
                {
                    AddColumn(layout, name, scalar, offset, size, ft);
                }
                else if (TryScalar(ft, out scalar, out size))
                {
                    AddColumn(layout, name, scalar, offset, size, null);
                }
                else if (ft.IsValueType && !IsCollection(ft) && depth < MaxDepth && !HasFixedBuffer(field))
                {
                    Flatten(layout, ft, name + ".", offset, path, depth + 1);
                }
                else
                {
                    layout.managed.Add(new RowLayout.ManagedField { name = name, path = path.ToArray() });
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        static void AddColumn(RowLayout layout, string name, string type, int offset, int size, Type enumType)
        {
            layout.columns.Add(new RowLayout.Column { name = name, type = type, offset = layout.rowSize, size = size, enumType = enumType });
            layout.segments.Add(new RowLayout.Segment { source = offset, target = layout.rowSize, size = size });
            layout.rowSize += size;
        }

        static void MergeSegments(RowLayout layout)
        {
            var merged = new List<RowLayout.Segment>();
            foreach (var s in layout.segments)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (last.source + last.size == s.source && last.target + last.size == s.target)
                    {
                        last.size += s.size;
                        merged[merged.Count - 1] = last;
                        continue;
                    }
                }
                merged.Add(s);
            }
            layout.segments.Clear();
            layout.segments.AddRange(merged);
        }

        static string DescribeRow(RowLayout layout)
        {
            var json = new JsonBuilder().BeginObject()
                .Prop("id", layout.id).Prop("kind", "row").Prop("name", TypeName(layout.type))
                .Prop("rowSize", layout.rowSize)
                .Key("columns").BeginArray();
            foreach (var c in layout.columns)
            {
                json.BeginObject().Prop("name", c.name).Prop("type", c.type).Prop("offset", c.offset);
                if (c.enumType != null)
                {
                    json.Prop("enum", TypeName(c.enumType)).Key("values").BeginObject();
                    foreach (var v in Enum.GetValues(c.enumType))
                        json.Prop(((Enum)v).ToString("D"), v.ToString());
                    json.EndObject();
                }
                json.EndObject();
            }
            json.EndArray().Key("managed").BeginArray();
            foreach (var m in layout.managed)
                json.BeginObject().Prop("name", m.name).Prop("type", TypeName(m.path[m.path.Length - 1].FieldType)).EndObject();
            return json.EndArray().EndObject().ToString();
        }

        static bool HasFixedBuffer(FieldInfo field) => field.IsDefined(typeof(FixedBufferAttribute), false);

        static bool IsCollection(Type type) => typeof(IEnumerable).IsAssignableFrom(type);

        static bool TryScalar(Type t, out string name, out int size)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: name = "bool"; size = 1; return true;
                case TypeCode.Byte: name = "u8"; size = 1; return true;
                case TypeCode.SByte: name = "i8"; size = 1; return true;
                case TypeCode.Int16: name = "i16"; size = 2; return true;
                case TypeCode.UInt16: name = "u16"; size = 2; return true;
                case TypeCode.Char: name = "char"; size = 2; return true;
                case TypeCode.Int32: name = "i32"; size = 4; return true;
                case TypeCode.UInt32: name = "u32"; size = 4; return true;
                case TypeCode.Int64: name = "i64"; size = 8; return true;
                case TypeCode.UInt64: name = "u64"; size = 8; return true;
                case TypeCode.Single: name = "f32"; size = 4; return true;
                case TypeCode.Double: name = "f64"; size = 8; return true;
            }
            name = null;
            size = 0;
            return false;
        }

        public static string CleanName(string name)
        {
            // Auto-property backing fields: "<speed>k__BackingField" -> "speed".
            if (name.Length > 0 && name[0] == '<')
            {
                int end = name.IndexOf('>');
                if (end > 1)
                    return name.Substring(1, end - 1);
            }
            return name;
        }

        public static string TypeName(Type type) => (type.FullName ?? type.Name).Replace(", ", ",");

        /// <summary>
        /// Writes a self-describing tagged value (see FORMAT.md, "Tagged values").
        /// </summary>
        readonly List<List<object>> _items = new List<List<object>>();
        readonly Dictionary<Type, TypedListWriter> _listWriters = new Dictionary<Type, TypedListWriter>();
        readonly RecordingBuffer _checkBuffer = new RecordingBuffer(256);
        bool _genericOnly;

        readonly Dictionary<Type, ObjectPlan> _valuePlans = new Dictionary<Type, ObjectPlan>();
        readonly Dictionary<Type, ObjectPlan> _objectPlans = new Dictionary<Type, ObjectPlan>();
        readonly Dictionary<long, string> _objectNames = new Dictionary<long, string>();

        /// <summary>Plan for a struct stored inline (a row field, a list element), or null.</summary>
        internal ObjectPlan GetValuePlan(Type type)
        {
            if (_valuePlans.TryGetValue(type, out var plan))
                return plan;
            _valuePlans[type] = null; // recursion guard
            plan = ObjectPlan.ForValue(this, type);
            _valuePlans[type] = plan;
            return plan;
        }

        ObjectPlan GetObjectPlan(Type type)
        {
            if (_objectPlans.TryGetValue(type, out var plan))
                return plan;
            _objectPlans[type] = null; // recursion guard
            bool located = type.IsValueType ? ObjectMemory.boxedBase >= 0 : ObjectMemory.classBase >= 0;
            plan = located ? ObjectPlan.ForObject(this, type) : null;
            _objectPlans[type] = plan;
            return plan;
        }

        internal ManagedFieldWriter[] GetManagedWriters(RowLayout layout)
        {
            if (layout.writers != null)
                return layout.writers;
            var writers = new ManagedFieldWriter[layout.managed.Count];
            for (int i = 0; i < writers.Length; i++)
            {
                try { writers[i] = ManagedFieldWriter.Create(this, layout.managed[i]); }
                catch { writers[i] = null; }
                if (writers[i] == null)
                    NoteReflection(layout.managed[i].path[layout.managed[i].path.Length - 1].FieldType,
                        $"field {layout.managed[i].name} of {TypeName(layout.type)}");
            }
            layout.writers = writers;
            return writers;
        }

        readonly HashSet<Type> _reflectionNoted = new HashSet<Type>();

        readonly List<string> _reflectionNotes = new List<string>();

        /// <summary>Remembers once per type that it is recorded through reflection, which allocates every tick.</summary>
        void NoteReflection(Type type, string where)
        {
            if (_reflectionNoted.Add(type))
                _reflectionNotes.Add($"{TypeName(type)} ({where})");
        }

        /// <summary>The types recorded through reflection (they make garbage), as text; empty when there are none.</summary>
        internal string DescribeReflection() => _reflectionNotes.Count == 0
            ? ""
            : "  recorded through reflection (makes garbage): " + string.Join(", ", _reflectionNotes) + "\n";

        bool WriteTypedList(RecordingBuffer buffer, object value, Type type, int depth, HashSet<object> visiting)
        {
            if (!_listWriters.TryGetValue(type, out var writer))
            {
                writer = typeof(IEnumerable).IsAssignableFrom(type) ? TypedListWriter.Create(this, type) : null;
                _listWriters[type] = writer;
            }
            if (writer == null || writer.disabled)
                return false;

            int start = buffer.length;
            if (!writer.Write(this, buffer, value, depth, visiting))
            {
                buffer.WriteByte(0);
                return true;
            }
            if (writer.NeedsSelfCheck(buffer.length - start <= 2))
                SelfCheck(writer, "list writer", buffer, start, value, type, depth, visiting);
            return true;
        }

        unsafe bool WriteObjectPlan(RecordingBuffer buffer, object value, Type type, int depth, HashSet<object> visiting)
        {
            var plan = GetObjectPlan(type);
            if (plan == null)
                NoteReflection(type, "object");
            if (plan == null || plan.disabled || depth + plan.depth > MaxDepth)
                return false;

            int start = buffer.length;
            byte* origin = ObjectMemory.Address(value) + (type.IsValueType ? ObjectMemory.boxedBase : ObjectMemory.classBase);
            plan.Write(origin, this, buffer, depth, visiting);
            if (plan.NeedsSelfCheck(false))
                SelfCheck(plan, "object plan", buffer, start, value, type, depth, visiting);
            return true;
        }

        /// <summary>
        /// A fast writer must produce exactly what the generic path does. Its first writes are redone generically and
        /// compared; on any difference the generic bytes replace its output and it is not used again.
        /// </summary>
        void SelfCheck(CheckedWriter writer, string what, RecordingBuffer buffer, int start, object value, Type type,
            int depth, HashSet<object> visiting)
        {
            _checkBuffer.Clear();
            _genericOnly = true;
            // The caller already marked a reference value as being visited; the check writes it again.
            bool unmarked = !type.IsValueType && visiting.Remove(value);
            try { WriteTagged(_checkBuffer, value, depth, visiting); }
            finally
            {
                _genericOnly = false;
                if (unmarked)
                    visiting.Add(value);
            }
            if (_checkBuffer.SameBytes(0, _checkBuffer.length, buffer.data, start, buffer.length - start))
                return;
            writer.disabled = true;
            UnityEngine.Debug.LogWarning($"[Recorder] The {what} for {TypeName(type)} disagreed with the generic path; using the generic path.");
            buffer.length = start;
            buffer.WriteBytes(_checkBuffer);
        }

        /// <summary>Writes a value through the generic path only, for self-checks of writers outside the registry.</summary>
        internal void WriteGeneric(RecordingBuffer buffer, object value, int depth, HashSet<object> visiting)
        {
            _genericOnly = true;
            try { WriteTagged(buffer, value, depth, visiting); }
            finally { _genericOnly = false; }
        }

        public void WriteTagged(RecordingBuffer buffer, object value, int depth, HashSet<object> visiting)
        {
            if (value == null)
            {
                buffer.WriteByte(0);
                return;
            }

            if (depth > MaxDepth)
            {
                buffer.WriteByte(12);
                return;
            }

            switch (value)
            {
                case bool b: buffer.WriteByte(b ? (byte)2 : (byte)1); return;
                case float f: buffer.WriteByte(5); buffer.WriteFloat(f); return;
                case double d: buffer.WriteByte(6); buffer.WriteDouble(d); return;
                case string s: buffer.WriteByte(7); buffer.WriteString(s); return;
                case byte[] bytes: buffer.WriteByte(13); buffer.WriteVarUInt((ulong)bytes.Length); buffer.WriteBytes(bytes, 0, bytes.Length); return;
                case UnityEngine.Object unityObject:
                {
                    // Reading a name allocates, so each object's name is read once per recording.
                    bool alive = unityObject;
                    long id = alive ? RecordingObjectId.Of(unityObject) : 0;
                    if (!alive)
                    {
                        buffer.WriteByte(10);
                        buffer.WriteString("");
                    }
                    else
                    {
                        if (!_objectNames.TryGetValue(id, out var name))
                            _objectNames[id] = name = unityObject.name;
                        buffer.WriteByte(10);
                        buffer.WriteString(name);
                    }
                    buffer.WriteVarInt(id);
                    return;
                }
            }

            var type = value.GetType();
            if (type.IsEnum)
            {
                buffer.WriteByte(3);
                buffer.WriteVarInt(Convert.ToInt64(value));
                return;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    buffer.WriteByte(3);
                    buffer.WriteVarInt(Convert.ToInt64(value));
                    return;
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Char:
                    buffer.WriteByte(4);
                    buffer.WriteVarUInt(Convert.ToUInt64(value));
                    return;
            }

            bool isReference = !type.IsValueType;
            if (isReference && !visiting.Add(value))
            {
                buffer.WriteByte(12);
                return;
            }

            try
            {
                if (value is IDictionary dictionary)
                {
                    buffer.WriteByte(11);
                    buffer.WriteVarUInt((ulong)dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        WriteTagged(buffer, entry.Key, depth + 1, visiting);
                        WriteTagged(buffer, entry.Value, depth + 1, visiting);
                    }
                    return;
                }

                if (!_genericOnly && WriteTypedList(buffer, value, type, depth, visiting))
                    return;

                if (value is IEnumerable enumerable)
                {
                    if (!_genericOnly)
                        NoteReflection(type, "collection");
                    // Struct collections such as a disposed DisposableList throw when enumerated, so the items are
                    // read before anything is written. Lists are read by index: no boxed enumerator, no new list.
                    while (_items.Count <= depth)
                        _items.Add(new List<object>());
                    var items = _items[depth];
                    items.Clear();
                    try
                    {
                        if (enumerable is IList list)
                        {
                            int count = list.Count;
                            for (int i = 0; i < count; i++)
                                items.Add(list[i]);
                        }
                        else
                        {
                            foreach (var item in enumerable)
                                items.Add(item);
                        }
                    }
                    catch
                    {
                        items.Clear();
                        buffer.WriteByte(0);
                        return;
                    }

                    buffer.WriteByte(8);
                    buffer.WriteVarUInt((ulong)items.Count);
                    for (int i = 0; i < items.Count; i++)
                        WriteTagged(buffer, items[i], depth + 1, visiting);
                    items.Clear();
                    return;
                }

                if (!_genericOnly && WriteObjectPlan(buffer, value, type, depth, visiting))
                    return;

                var layout = GetObject(type);
                buffer.WriteByte(9);
                buffer.WriteVarUInt((ulong)layout.id);
                foreach (var field in layout.fields)
                    WriteTagged(buffer, field.GetValue(value), depth + 1, visiting);
            }
            finally
            {
                if (isReference)
                    visiting.Remove(value);
            }
        }
    }

    /// <summary>Identity comparer for cycle detection in tagged values.</summary>
    public sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer instance = new ReferenceComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }
}
