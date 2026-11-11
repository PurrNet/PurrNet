using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;

namespace PurrNet.Recording
{
    /// <summary>
    /// Where managed objects keep their fields, found once by experiment so plans can read objects in place.
    /// Unity's garbage collector does not move objects, so an object's address stays valid while it is referenced.
    /// </summary>
    static unsafe class ObjectMemory
    {
        sealed class ClassProbe { public long a; public long b; }
        struct StructProbe { public long a; public long b; }

        const long MarkA = 0x0123456789ABCDEF;
        const long MarkB = 0x7EDCBA9876543210;

        /// <summary>Bytes from a class object's address to where <see cref="UnsafeUtility.GetFieldOffset"/> counts from.</summary>
        public static readonly int classBase = -1;
        /// <summary>Bytes from a boxed struct's address to the struct's data.</summary>
        public static readonly int boxedBase = -1;

        static ObjectMemory()
        {
            try
            {
                var probe = new ClassProbe { a = MarkA, b = MarkB };
                classBase = Find(probe, typeof(ClassProbe));
                object boxed = new StructProbe { a = MarkA, b = MarkB };
                boxedBase = Find(boxed, typeof(StructProbe));
                GC.KeepAlive(probe);
                GC.KeepAlive(boxed);
            }
            catch
            {
                classBase = boxedBase = -1;
            }
            listVersions = CheckListVersions();
        }

        static int Find(object o, Type type)
        {
            int offsetA = UnsafeUtility.GetFieldOffset(type.GetField("a"));
            int offsetB = UnsafeUtility.GetFieldOffset(type.GetField("b"));
            byte* p = Address(o);
            for (int at = 0; at <= 64; at += 4)
            {
                if (*(long*)(p + at) == MarkA)
                {
                    int origin = at - offsetA;
                    return *(long*)(p + origin + offsetB) == MarkB ? origin : -1;
                }
            }
            return -1;
        }

        public static byte* Address(object o) => (byte*)UnsafeUtility.As<object, IntPtr>(ref o);

        public static object ReadReference(byte* at)
        {
            var pointer = *(IntPtr*)at;
            return UnsafeUtility.As<IntPtr, object>(ref pointer);
        }

        /// <summary>Whether a List's change counter can be read in place (checked once on a list of ints).</summary>
        public static readonly bool listVersions;

        /// <summary>Where a List type keeps the counter it raises on every change, or -1.</summary>
        public static int ListVersionOffset(Type listType)
        {
            var field = listType.GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
            return classBase >= 0 && field != null && field.FieldType == typeof(int)
                ? classBase + UnsafeUtility.GetFieldOffset(field)
                : -1;
        }

        static bool CheckListVersions()
        {
            try
            {
                int offset = ListVersionOffset(typeof(List<int>));
                if (offset < 0)
                    return false;
                var list = new List<int>();
                int before = *(int*)(Address(list) + offset);
                list.Add(1);
                int added = *(int*)(Address(list) + offset);
                list[0] = 2;
                int set = *(int*)(Address(list) + offset);
                list.Clear();
                int cleared = *(int*)(Address(list) + offset);
                GC.KeepAlive(list);
                return added != before && set != added && cleared != set;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Something that writes tagged values without reflection, re-checked against the generic path at first.</summary>
    abstract class CheckedWriter
    {
        const int SelfChecks = 4;
        int _checksLeft = SelfChecks;
        int _trivialChecksLeft = 16;
        public bool disabled;

        /// <summary>Checks until <see cref="SelfChecks"/> non-trivial values matched; trivial ones prove little.</summary>
        public bool NeedsSelfCheck(bool trivial)
        {
            if (_checksLeft <= 0)
                return false;
            if (trivial)
                return _trivialChecksLeft-- > 0;
            _checksLeft--;
            return true;
        }
    }

    /// <summary>
    /// The tagged encoding of a value as a flat list of reads from its memory. Primitive fields are read in place,
    /// references are handed to <see cref="RecordingTypeRegistry.WriteTagged"/> (no boxing for those), and struct
    /// fields that are lists go to a typed list writer.
    /// </summary>
    sealed class ObjectPlan : CheckedWriter
    {
        enum Op : byte { Object, Bool, F32, F64, Signed, Unsigned, Enum, Nullable, Reference, InlineList }

        struct Step
        {
            public Op op;
            public int offset;
            public int size;
            public bool signed;
            public int layoutId;
            /// <summary>Nullable: steps of the value, skipped when it has none.</summary>
            public int skip;
            /// <summary>Nesting below the plan's root (its fields are at 1).</summary>
            public int level;
            public InlineListWriter list;
        }

        readonly List<Step> _steps = new List<Step>();
        /// <summary>Deepest level written directly; deeper values make the caller use the generic path.</summary>
        public int depth;

        /// <summary>
        /// True when the encoding depends on nothing but the value's own bytes (no references, no lists), so equal
        /// bytes always encode the same.
        /// </summary>
        public bool readsOnlyItself
        {
            get
            {
                foreach (var s in _steps)
                    if (s.op == Op.Reference || s.op == Op.InlineList)
                        return false;
                return true;
            }
        }

        /// <summary>A struct value stored inline (a row field or a list element).</summary>
        public static ObjectPlan ForValue(RecordingTypeRegistry types, Type type)
        {
            var plan = new ObjectPlan();
            return type.IsValueType && plan.AddValue(types, type, 0, 0) ? plan : null;
        }

        /// <summary>The fields of a class object or boxed struct, counting offsets from its field origin.</summary>
        public static ObjectPlan ForObject(RecordingTypeRegistry types, Type type)
        {
            var plan = new ObjectPlan();
            return plan.AddObject(types, type, 0, 0) ? plan : null;
        }

        bool AddValue(RecordingTypeRegistry types, Type type, int offset, int level)
        {
            if (level > RecordingTypeRegistry.MaxDepth)
                return false;
            depth = Math.Max(depth, level);

            if (!type.IsValueType)
            {
                _steps.Add(new Step { op = Op.Reference, offset = offset, level = level });
                return true;
            }

            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                if (underlying == typeof(ulong))
                    return false; // the generic path converts to long, which can throw
                _steps.Add(new Step { op = Op.Enum, offset = offset, size = UnsafeUtility.SizeOf(underlying), signed = IsSigned(underlying), level = level });
                return true;
            }

            // A boxed Nullable<T> is null or a boxed T, so the generic path writes null or the plain value.
            var inner = Nullable.GetUnderlyingType(type);
            if (inner != null)
            {
                var hasValue = type.GetField("hasValue", BindingFlags.Instance | BindingFlags.NonPublic);
                var value = type.GetField("value", BindingFlags.Instance | BindingFlags.NonPublic);
                if (hasValue == null || value == null)
                    return false;
                int at = _steps.Count;
                _steps.Add(new Step { op = Op.Nullable, offset = offset + UnsafeUtility.GetFieldOffset(hasValue), level = level });
                if (!AddValue(types, inner, offset + UnsafeUtility.GetFieldOffset(value), level))
                    return false;
                var step = _steps[at];
                step.skip = _steps.Count - at - 1;
                _steps[at] = step;
                return true;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    _steps.Add(new Step { op = Op.Bool, offset = offset, size = 1, level = level });
                    return true;
                case TypeCode.Single:
                    _steps.Add(new Step { op = Op.F32, offset = offset, size = 4, level = level });
                    return true;
                case TypeCode.Double:
                    _steps.Add(new Step { op = Op.F64, offset = offset, size = 8, level = level });
                    return true;
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    _steps.Add(new Step { op = Op.Signed, offset = offset, size = UnsafeUtility.SizeOf(type), signed = true, level = level });
                    return true;
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Char:
                    _steps.Add(new Step { op = Op.Unsigned, offset = offset, size = type == typeof(char) ? 2 : UnsafeUtility.SizeOf(type), level = level });
                    return true;
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
                return false;
            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                var list = InlineListWriter.Create(types, type);
                if (list == null)
                    return false;
                _steps.Add(new Step { op = Op.InlineList, offset = offset, list = list, level = level });
                return true;
            }

            return AddObject(types, type, offset, level);
        }

        bool AddObject(RecordingTypeRegistry types, Type type, int offset, int level)
        {
            if (level > RecordingTypeRegistry.MaxDepth || typeof(IEnumerable).IsAssignableFrom(type) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
                type == typeof(string) || type.IsArray || type.IsPointer || type.IsPrimitive || type.IsEnum)
                return false;
            depth = Math.Max(depth, level);

            var layout = types.GetObject(type);
            _steps.Add(new Step { op = Op.Object, layoutId = layout.id, level = level });
            foreach (var field in layout.fields)
            {
                if (!AddValue(types, field.FieldType, offset + UnsafeUtility.GetFieldOffset(field), level + 1))
                    return false;
            }
            return true;
        }

        static bool IsSigned(Type t) => t == typeof(sbyte) || t == typeof(short) || t == typeof(int) || t == typeof(long);

        /// <summary>Writes the value at <paramref name="p"/> as the generic path would at <paramref name="baseDepth"/>.</summary>
        public unsafe void Write(byte* p, RecordingTypeRegistry types, RecordingBuffer buffer, int baseDepth,
            HashSet<object> visiting)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                var s = _steps[i];
                byte* at = p + s.offset;
                switch (s.op)
                {
                    case Op.Nullable:
                        if (*at == 0)
                        {
                            buffer.WriteByte(0);
                            i += s.skip;
                        }
                        break;
                    case Op.Object:
                        buffer.WriteByte(9);
                        buffer.WriteVarUInt((ulong)s.layoutId);
                        break;
                    case Op.Bool:
                        buffer.WriteByte(*at != 0 ? (byte)2 : (byte)1);
                        break;
                    case Op.F32:
                        buffer.WriteByte(5);
                        buffer.WriteFloat(*(float*)at);
                        break;
                    case Op.F64:
                        buffer.WriteByte(6);
                        buffer.WriteDouble(*(double*)at);
                        break;
                    case Op.Signed:
                        buffer.WriteByte(3);
                        buffer.WriteVarInt(ReadSigned(at, s.size));
                        break;
                    case Op.Unsigned:
                        buffer.WriteByte(4);
                        buffer.WriteVarUInt(ReadUnsigned(at, s.size));
                        break;
                    case Op.Enum:
                        buffer.WriteByte(3);
                        buffer.WriteVarInt(s.signed ? ReadSigned(at, s.size) : (long)ReadUnsigned(at, s.size));
                        break;
                    case Op.Reference:
                        types.WriteTagged(buffer, ObjectMemory.ReadReference(at), baseDepth + s.level, visiting);
                        break;
                    case Op.InlineList:
                        if (!s.list.WriteAt(types, buffer, at, baseDepth + s.level, visiting))
                            buffer.WriteByte(0);
                        break;
                }
            }
        }

        static unsafe long ReadSigned(byte* at, int size)
        {
            switch (size)
            {
                case 1: return *(sbyte*)at;
                case 2: return *(short*)at;
                case 4: return *(int*)at;
                default: return *(long*)at;
            }
        }

        static unsafe ulong ReadUnsigned(byte* at, int size)
        {
            switch (size)
            {
                case 1: return *at;
                case 2: return *(ushort*)at;
                case 4: return *(uint*)at;
                default: return *(ulong*)at;
            }
        }
    }

    /// <summary>Finds the single <see cref="IList{T}"/> a type implements.</summary>
    static class ListTypes
    {
        public static Type Element(Type listType)
        {
            Type element = null;
            foreach (var i in listType.GetInterfaces())
            {
                if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(IList<>))
                    continue;
                if (element != null)
                    return null; // ambiguous
                element = i.GetGenericArguments()[0];
            }
            return element;
        }
    }

    /// <summary>Writes one list element: plain structs through their plan, everything else as a tagged value.</summary>
    static class ElementWriter<T>
    {
        public static unsafe void Write(ObjectPlan plan, RecordingTypeRegistry types, RecordingBuffer buffer, ref T item,
            int depth, HashSet<object> visiting)
        {
            if (plan != null && depth + plan.depth <= RecordingTypeRegistry.MaxDepth)
            {
                // Plans exist only for structs, so this is the item's own memory (a local, it does not move).
                fixed (byte* p = &UnsafeUtility.As<T, byte>(ref item))
                    plan.Write(p, types, buffer, depth, visiting);
            }
            else
                types.WriteTagged(buffer, item, depth, visiting);
        }
    }

    interface IElementPlan
    {
        ObjectPlan plan { set; }
    }

    /// <summary>
    /// Writes an <see cref="IList{T}"/> object (a List, an array, a boxed DisposableList) as a tagged list, by index,
    /// without a boxed enumerator.
    /// </summary>
    abstract class TypedListWriter : CheckedWriter
    {
        /// <summary>Returns false when the list could not be read (the caller writes null, as the generic path does).</summary>
        public abstract bool Write(RecordingTypeRegistry types, RecordingBuffer buffer, object list, int depth,
            HashSet<object> visiting);

        public static TypedListWriter Create(RecordingTypeRegistry types, Type listType)
        {
            var element = ListTypes.Element(listType);
            if (element == null)
                return null;
            try
            {
                var writer = (TypedListWriter)Activator.CreateInstance(typeof(ListWriter<>).MakeGenericType(element));
                ((IElementPlan)writer).plan = element.IsValueType ? types.GetValuePlan(element) : null;
                return writer;
            }
            catch
            {
                // AOT platforms may not have the generic instantiation; the generic path still works.
                return null;
            }
        }
    }

    sealed class ListWriter<T> : TypedListWriter, IElementPlan
    {
        ObjectPlan _plan;
        ElementMemo _memo;

        public ObjectPlan plan
        {
            get => _plan;
            set
            {
                _plan = value;
                _memo = ElementMemo.For<T>(value);
            }
        }

        public override bool Write(RecordingTypeRegistry types, RecordingBuffer buffer, object value, int depth,
            HashSet<object> visiting)
        {
            if (value is PurrNet.Pooling.DisposableList<T> disposable && disposable.isDisposed)
                return false;
            var list = (IList<T>)value;
            int count;
            try
            {
                // Read everything first: a disposed list throws, and then nothing may have been written.
                count = list.Count;
                for (int i = 0; i < count; i++)
                    _ = list[i];
            }
            catch
            {
                return false;
            }

            buffer.WriteByte(8);
            buffer.WriteVarUInt((ulong)count);
            var memo = ElementMemo.Use(_memo, _plan, count, depth + 1);
            for (int i = 0; i < count; i++)
            {
                T item = list[i];
                if (memo != null)
                    memo.Write(ref item, _plan, types, buffer, depth + 1, visiting);
                else
                    ElementWriter<T>.Write(_plan, types, buffer, ref item, depth + 1, visiting);
            }
            memo?.End();
            return true;
        }
    }

    /// <summary>A struct list (a DisposableList) stored inline in another value, read in place without boxing it.</summary>
    abstract class InlineListWriter
    {
        public abstract unsafe bool WriteAt(RecordingTypeRegistry types, RecordingBuffer buffer, byte* at, int depth,
            HashSet<object> visiting);

        public static InlineListWriter Create(RecordingTypeRegistry types, Type listType)
        {
            if (!listType.IsValueType)
                return null;
            var element = ListTypes.Element(listType);
            if (element == null)
                return null;
            try
            {
                var writer = (InlineListWriter)Activator.CreateInstance(
                    typeof(InlineListWriter<,>).MakeGenericType(listType, element));
                ((IElementPlan)writer).plan = element.IsValueType ? types.GetValuePlan(element) : null;
                return writer;
            }
            catch
            {
                return null;
            }
        }
    }

    sealed class InlineListWriter<TList, T> : InlineListWriter, IElementPlan where TList : struct, IList<T>
    {
        static readonly bool _disposable = typeof(TList) == typeof(PurrNet.Pooling.DisposableList<T>);

        ObjectPlan _plan;
        ElementMemo _memo;

        public ObjectPlan plan
        {
            get => _plan;
            set
            {
                _plan = value;
                _memo = ElementMemo.For<T>(value);
            }
        }

        public override unsafe bool WriteAt(RecordingTypeRegistry types, RecordingBuffer buffer, byte* at, int depth,
            HashSet<object> visiting)
        {
            // The generic path writes deeper values as a depth marker; leave those to it.
            if (depth > RecordingTypeRegistry.MaxDepth)
            {
                buffer.WriteByte(12);
                return true;
            }

            // Constrained calls on the struct in place: no box.
            ref TList list = ref UnsafeUtility.AsRef<TList>(at);
            // A disposed DisposableList throws on every read; exceptions allocate, so ask first.
            if (_disposable && UnsafeUtility.As<TList, PurrNet.Pooling.DisposableList<T>>(ref list).isDisposed)
                return false;
            int count;
            try
            {
                count = list.Count;
                for (int i = 0; i < count; i++)
                    _ = list[i];
            }
            catch
            {
                return false;
            }

            buffer.WriteByte(8);
            buffer.WriteVarUInt((ulong)count);
            var memo = ElementMemo.Use(_memo, _plan, count, depth + 1);
            for (int i = 0; i < count; i++)
            {
                T item = list[i];
                if (memo != null)
                    memo.Write(ref item, _plan, types, buffer, depth + 1, visiting);
                else
                    ElementWriter<T>.Write(_plan, types, buffer, ref item, depth + 1, visiting);
            }
            memo?.End();
            return true;
        }
    }

    /// <summary>
    /// Remembers how the previous list's elements were encoded, by their bytes. Lists that mostly repeat from one tick
    /// to the next (the hierarchy's spawned prefabs, an inventory) then copy each unchanged element's encoding instead
    /// of building it again. Only for unmanaged elements whose plan reads nothing but their own bytes, so equal bytes
    /// mean an equal encoding; the output is the same as without it.
    /// </summary>
    sealed unsafe class ElementMemo
    {
        readonly int _size;
        byte[] _raw = Array.Empty<byte>(), _nextRaw = Array.Empty<byte>();
        int[] _start = new int[1], _nextStart = new int[1];
        RecordingBuffer _encoded = new RecordingBuffer(ByteShelf.shared, 256), _nextEncoded = new RecordingBuffer(ByteShelf.shared, 256);
        int _count, _nextCount, _cursor, _depth = -1;

        ElementMemo(int size) => _size = size;

        public static ElementMemo For<T>(ObjectPlan plan) =>
            plan != null && plan.readsOnlyItself && typeof(T).IsValueType && UnsafeUtility.IsUnmanaged(typeof(T)) &&
            UnsafeUtility.SizeOf(typeof(T)) > 0
                ? new ElementMemo(UnsafeUtility.SizeOf(typeof(T)))
                : null;

        /// <summary>The memo to use for a list of <paramref name="count"/> items at <paramref name="depth"/>, or null.</summary>
        public static ElementMemo Use(ElementMemo memo, ObjectPlan plan, int count, int depth)
        {
            // Deeper items go through the generic path (see ElementWriter), which the memo does not reproduce.
            if (memo == null || depth + plan.depth > RecordingTypeRegistry.MaxDepth)
                return null;
            memo.Begin(count, depth);
            return memo;
        }

        void Begin(int count, int depth)
        {
            if (depth != _depth)
            {
                _count = 0;
                _depth = depth;
            }
            _cursor = 0;
            _nextCount = 0;
            _nextEncoded.Clear();
            // Power-of-two sizes: lists that grow over a match reach their size in few steps.
            if (_nextRaw.Length < count * _size)
                _nextRaw = new byte[Math.Max(UnityEngine.Mathf.NextPowerOfTwo(count * _size), _nextRaw.Length * 2)];
            if (_nextStart.Length < count + 1)
                _nextStart = new int[Math.Max(UnityEngine.Mathf.NextPowerOfTwo(count + 1), _nextStart.Length * 2)];
        }

        public void Write<T>(ref T item, ObjectPlan plan, RecordingTypeRegistry types, RecordingBuffer buffer, int depth,
            HashSet<object> visiting)
        {
            // Memos exist only for structs, so this is the item's own memory (a local, it does not move).
            fixed (byte* p = &UnsafeUtility.As<T, byte>(ref item))
                Write(p, plan, types, buffer, depth, visiting);
        }

        void Write(byte* p, ObjectPlan plan, RecordingTypeRegistry types, RecordingBuffer buffer, int depth,
            HashSet<object> visiting)
        {
            int before = buffer.length;
            int match = Find(p);
            if (match >= 0)
            {
                buffer.WriteBytes(_encoded.data, _start[match], _start[match + 1] - _start[match]);
                _cursor = match + 1;
            }
            else
                plan.Write(p, types, buffer, depth, visiting);

            fixed (byte* raw = _nextRaw)
                UnsafeUtility.MemCpy(raw + _nextCount * _size, p, _size);
            _nextEncoded.WriteBytes(buffer.data, before, buffer.length - before);
            _nextStart[++_nextCount] = _nextEncoded.length;
        }

        /// <summary>
        /// The previous list's element equal to the one at <paramref name="p"/>, looked for where this list has got to
        /// in it: the same position, or the next one when an element was removed before it.
        /// </summary>
        int Find(byte* p)
        {
            if (_cursor >= _count)
                return -1;
            fixed (byte* raw = _raw)
            {
                int end = Math.Min(_count, _cursor + 2);
                for (int j = _cursor; j < end; j++)
                    if (UnsafeUtility.MemCmp(p, raw + j * _size, _size) == 0)
                        return j;
            }
            return -1;
        }

        public void End()
        {
            (_raw, _nextRaw) = (_nextRaw, _raw);
            (_start, _nextStart) = (_nextStart, _start);
            (_encoded, _nextEncoded) = (_nextEncoded, _encoded);
            _count = _nextCount;
        }
    }

    /// <summary>
    /// Writes one managed field of a row in place: a reference is read from the row and written as a tagged value,
    /// a struct (a DisposableList, say) through its plan. No box of the row or the field.
    /// </summary>
    sealed class ManagedFieldWriter : CheckedWriter
    {
        readonly int _offset;
        readonly bool _reference;
        readonly ObjectPlan _plan;
        // Where the field's List keeps its change counter; -1 for a string (it never changes), -2 when the field is
        // neither or the list's items can change without the list knowing (classes, structs holding references).
        readonly int _version = -2;

        ManagedFieldWriter(int offset, bool reference, ObjectPlan plan, Type type = null)
        {
            _offset = offset;
            _reference = reference;
            _plan = plan;
            if (type == typeof(string))
                _version = -1;
            else if (type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) &&
                     ObjectMemory.listVersions)
            {
                var item = type.GetGenericArguments()[0];
                int at = item == typeof(string) || UnsafeUtility.IsUnmanaged(item) ? ObjectMemory.ListVersionOffset(type) : -1;
                if (at >= 0)
                    _version = at;
            }
        }

        /// <summary>
        /// The object the field holds and its change counter, when both being the same as before means the value is
        /// the same: a string, or a list of strings or plain data. False for anything else and for null.
        /// </summary>
        public unsafe bool Identify(byte* row, out object source, out int version)
        {
            source = null;
            version = 0;
            if (_version == -2)
                return false;
            source = ObjectMemory.ReadReference(row + _offset);
            if (source == null)
                return false;
            if (_version >= 0)
                version = *(int*)(ObjectMemory.Address(source) + _version);
            return true;
        }

        public static ManagedFieldWriter Create(RecordingTypeRegistry types, RowLayout.ManagedField field)
        {
            int offset = 0;
            foreach (var f in field.path)
                offset += UnsafeUtility.GetFieldOffset(f);
            var type = field.path[field.path.Length - 1].FieldType;
            if (!type.IsValueType)
                return new ManagedFieldWriter(offset, true, null, type);
            var plan = types.GetValuePlan(type);
            return plan != null && plan.depth <= RecordingTypeRegistry.MaxDepth ? new ManagedFieldWriter(offset, false, plan) : null;
        }

        public unsafe void Write(byte* row, RecordingTypeRegistry types, RecordingBuffer buffer, HashSet<object> visiting)
        {
            if (_reference)
                types.WriteTagged(buffer, ObjectMemory.ReadReference(row + _offset), 0, visiting);
            else
                _plan.Write(row + _offset, types, buffer, 0, visiting);
        }
    }
}
