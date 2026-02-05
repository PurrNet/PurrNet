using System;
using Unity.Collections;

namespace PurrNet.Packing
{
    /// <summary>Myers diff algorithm using only unmanaged collections (NativeArray, NativeList). Result and internal buffers use the given allocator; caller must dispose.</summary>
    public static class MyersDiffNative
    {
        public static NativeList<DiffOpNative<T>> Diff<T>(NativeList<T> a, NativeList<T> b, Allocator allocator) where T : unmanaged
        {
            int n = a.IsCreated ? a.Length : 0;
            int m = b.IsCreated ? b.Length : 0;

            if (n == 0 && m == 0)
                return new NativeList<DiffOpNative<T>>(allocator);

            if (n == 0)
            {
                var res = new NativeList<DiffOpNative<T>>(1, allocator);
                var bCopy = new NativeList<T>(m, allocator);
                for (int i = 0; i < m; i++)
                    bCopy.Add(b[i]);
                res.Add(new DiffOpNative<T>(OperationType.Add, 0, 0, bCopy));
                return res;
            }

            if (m == 0)
            {
                var res = new NativeList<DiffOpNative<T>>(1, allocator);
                res.Add(new DiffOpNative<T>(OperationType.Delete, 0, n));
                return res;
            }

            int max = n + m;
            int size = 2 * max + 1;

            var trace = new NativeList<NativeArray<int>>(max + 1, allocator);
            var v = new NativeArray<int>(size, allocator);

            try
            {
                for (int d = 0; d <= max; d++)
                {
                    var vCopy = new NativeArray<int>(size, allocator);
                    NativeArray<int>.Copy(v, vCopy);
                    trace.Add(vCopy);

                    for (int k = -d; k <= d; k += 2)
                    {
                        int kIndex = k + max;
                        int x;
                        if (k == -d || (k != d && v[kIndex - 1] < v[kIndex + 1]))
                            x = v[kIndex + 1];
                        else
                            x = v[kIndex - 1] + 1;

                        int y = x - k;
                        while (x < n && y < m && PurrEquality<T>.Default.Equals(a[x], b[y]))
                        {
                            x++;
                            y++;
                        }

                        v[kIndex] = x;

                        if (x >= n && y >= m)
                        {
                            var res = Backtrack(a, b, trace, d, max, n, m, allocator);
                            v.Dispose();
                            for (int i = 0; i < trace.Length; i++)
                                trace[i].Dispose();
                            trace.Dispose();
                            return res;
                        }
                    }
                }

                v.Dispose();
                for (int i = 0; i < trace.Length; i++)
                    trace[i].Dispose();
                trace.Dispose();
                return new NativeList<DiffOpNative<T>>(allocator);
            }
            catch
            {
                v.Dispose();
                for (int i = 0; i < trace.Length; i++)
                    trace[i].Dispose();
                trace.Dispose();
                throw;
            }
        }

        static NativeList<DiffOpNative<T>> Backtrack<T>(
            NativeList<T> a, NativeList<T> b,
            NativeList<NativeArray<int>> trace,
            int d, int offset, int n, int m,
            Allocator allocator) where T : unmanaged
        {
            var elementOps = new NativeList<DiffOpNative<T>>(d + 1, allocator);
            int x = n;
            int y = m;

            for (int depth = d; depth >= 0; depth--)
            {
                var vArr = trace[depth];
                int k = x - y;
                int kIdx = k + offset;

                int prevK, prevX;
                bool down;
                if (k == -depth || (k != depth && vArr[kIdx - 1] < vArr[kIdx + 1]))
                {
                    prevK = k + 1;
                    prevX = vArr[prevK + offset];
                    down = true;
                }
                else
                {
                    prevK = k - 1;
                    prevX = vArr[prevK + offset];
                    down = false;
                }

                int prevY = prevX - prevK;

                while (x > prevX && y > prevY)
                {
                    x--;
                    y--;
                }

                if (depth > 0)
                {
                    if (down)
                    {
                        y--;
                        var values = new NativeList<T>(1, allocator);
                        values.Add(b[y]);
                        elementOps.Add(new DiffOpNative<T>(
                            x == n ? OperationType.Add : OperationType.Insert,
                            x, 0, values));
                    }
                    else
                    {
                        x--;
                        elementOps.Add(new DiffOpNative<T>(OperationType.Delete, x, 1));
                    }
                }
            }

            Reverse(elementOps);
            var result = MergeOps(elementOps, allocator);
            for (int i = 0; i < elementOps.Length; i++)
                elementOps[i].Dispose();
            elementOps.Dispose();
            return result;
        }

        static void Reverse<T>(NativeList<DiffOpNative<T>> list) where T : unmanaged
        {
            int half = list.Length / 2;
            for (int i = 0; i < half; i++)
            {
                int j = list.Length - 1 - i;
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }

        static NativeList<DiffOpNative<T>> MergeOps<T>(NativeList<DiffOpNative<T>> ops, Allocator allocator) where T : unmanaged
        {
            var result = new NativeList<DiffOpNative<T>>(ops.Length, allocator);

            for (int i = 0; i < ops.Length; i++)
            {
                var op = ops[i];

                switch (op.type)
                {
                    case OperationType.Delete:
                    {
                        int idx = op.index;
                        int len = op.length;
                        while (i + 1 < ops.Length && ops[i + 1].type == OperationType.Delete &&
                               ops[i + 1].index == idx + len)
                        {
                            len += ops[i + 1].length;
                            i++;
                        }
                        result.Add(new DiffOpNative<T>(OperationType.Delete, idx, len));
                        continue;
                    }
                    case OperationType.Insert:
                    case OperationType.Add:
                    {
                        var vals = new NativeList<T>(op.values.IsCreated ? op.values.Length : 0, allocator);
                        if (op.values.IsCreated)
                        {
                            for (int j = 0; j < op.values.Length; j++)
                                vals.Add(op.values[j]);
                        }
                        int idx = op.index;
                        bool isAdd = op.type == OperationType.Add;

                        while (i + 1 < ops.Length &&
                               ops[i + 1].type == op.type &&
                               (isAdd || ops[i + 1].index == idx))
                        {
                            var next = ops[i + 1].values;
                            if (next.IsCreated)
                            {
                                for (int j = 0; j < next.Length; j++)
                                    vals.Add(next[j]);
                            }
                            i++;
                        }
                        result.Add(new DiffOpNative<T>(op.type, idx, 0, vals));
                        continue;
                    }
                    default:
                        result.Add(op);
                        break;
                }
            }

            return result;
        }

        public static void Apply<T>(NativeList<T> list, NativeList<DiffOpNative<T>> ops) where T : unmanaged
        {
            int offset = 0;
            for (int i = 0; i < ops.Length; i++)
            {
                var op = ops[i];
                switch (op.type)
                {
                    case OperationType.Add:
                        if (op.values.IsCreated)
                        {
                            for (int j = 0; j < op.values.Length; j++)
                                list.Add(op.values[j]);
                            offset += op.values.Length;
                        }
                        break;
                    case OperationType.Insert:
                        if (op.values.IsCreated && op.values.Length > 0)
                        {
                            int insertIndex = op.index + offset;
                            InsertRange(list, insertIndex, op.values);
                            offset += op.values.Length;
                        }
                        break;
                    case OperationType.Delete:
                        list.RemoveRange(op.index + offset, op.length);
                        offset -= op.length;
                        break;
                    case OperationType.End:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        static void InsertRange<T>(NativeList<T> list, int index, NativeList<T> values) where T : unmanaged
        {
            if (values.Length == 0) return;
            // Use Persistent and dispose immediately to avoid Temp lifetime issues when list is Persistent
            var temp = new NativeList<T>(list.Length + values.Length, Allocator.Persistent);
            try
            {
                for (int i = 0; i < index; i++)
                    temp.Add(list[i]);
                for (int i = 0; i < values.Length; i++)
                    temp.Add(values[i]);
                for (int i = index; i < list.Length; i++)
                    temp.Add(list[i]);
                list.Clear();
                for (int i = 0; i < temp.Length; i++)
                    list.Add(temp[i]);
            }
            finally
            {
                temp.Dispose();
            }
        }
    }
}
