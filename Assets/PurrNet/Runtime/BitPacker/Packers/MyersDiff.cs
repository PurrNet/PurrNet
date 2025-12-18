using System;
using System.Collections.Generic;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class MyersDiff
    {
        public static DisposableList<DiffOp<T>> Diff<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
        {
            switch (a.Count)
            {
                case 0 when b.Count == 0:
                    return DisposableList<DiffOp<T>>.Create();
                case 0:
                {
                    var res = DisposableList<DiffOp<T>>.Create();
                    if (b.Count == 0)
                        return res;
                    var bList = DisposableList<T>.Create(b);
                    res.Add(new DiffOp<T>(OperationType.Add, 0, 0, bList));
                    return res;
                }
            }

            if (b.Count == 0)
            {
                var res = DisposableList<DiffOp<T>>.Create();
                res.Add(new DiffOp<T>(OperationType.Delete, 0, a.Count));
                return res;
            }

            int n = a.Count, m = b.Count;
            int max = n + m;
            int size = 2 * max + 1;       // total array size per wavefront

            var trace = DisposableList<DisposableArray<int>>.Create(max + 1);
            var v = DisposableArray<int>.Create(size);

            // Forward search
            for (int d = 0; d <= max; d++)
            {
                trace.Add(DisposableArray<int>.Create(v));

                for (int k = -d; k <= d; k += 2)
                {
                    int kIndex = k + max;
                    int x;
                    if (k == -d || (k != d && v[kIndex - 1] < v[kIndex + 1]))
                        x = v[kIndex + 1];   // down
                    else
                        x = v[kIndex - 1] + 1; // right

                    int y = x - k;
                    // follow diagonal (the "snake")
                    while (x < n && y < m && Packer.AreEqual(a[x], b[y]))
                    {
                        x++;
                        y++;
                    }

                    v[kIndex] = x;

                    if (x >= n && y >= m)
                    {
                        var res = Backtrack(a, b, trace, d, max);
                        v.Dispose();
                        for (var i = 0; i < trace.Count; i++)
                            trace[i].Dispose();
                        trace.Dispose();
                        return res;
                    }
                }
            }

            v.Dispose();
            for (var i = 0; i < trace.Count; i++)
                trace[i].Dispose();
            trace.Dispose();
            return DisposableList<DiffOp<T>>.Create();
        }

        private static DisposableList<DiffOp<T>> Backtrack<T>(
            IReadOnlyList<T> a,
            IReadOnlyList<T> b,
            DisposableList<DisposableArray<int>> trace,
            int d,
            int offset)
        {
            var elementOps = DisposableList<DiffOp<T>>.Create();
            int x = a.Count;
            int y = b.Count;

            for (int depth = d; depth >= 0; depth--)
            {
                var v = trace[depth];
                int k = x - y;
                int kIdx = k + offset;

                int prevK, prevX;
                bool down;
                if (k == -depth || (k != depth && v[kIdx - 1] < v[kIdx + 1]))
                {
                    // down (insert)
                    prevK = k + 1;
                    prevX = v[prevK + offset];
                    down = true;
                }
                else
                {
                    // right (delete)
                    prevK = k - 1;
                    prevX = v[prevK + offset];
                    down = false;
                }

                int prevY = prevX - prevK;

                // move along diagonal (matches)
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
                        var values = DisposableList<T>.Create();
                        values.Add(b[y]);
                        elementOps.Add(new DiffOp<T>(
                            x == a.Count ? OperationType.Add : OperationType.Insert,
                            x,
                            0,
                            values));
                    }
                    else
                    {
                        x--;
                        elementOps.Add(new DiffOp<T>(OperationType.Delete, x, 1));
                    }
                }
            }

            elementOps.Reverse();

            // merge to ranges/Replace
            return MergeOps(elementOps);
        }

        private static DisposableList<DiffOp<T>> MergeOps<T>(DisposableList<DiffOp<T>> ops)
        {
            var result = DisposableList<DiffOp<T>>.Create();

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];

                switch (op.type)
                {
                    // Merge consecutive Deletes
                    case OperationType.Delete:
                    {
                        int idx = op.index;
                        int len = op.length;
                        while (i + 1 < ops.Count && ops[i + 1].type == OperationType.Delete &&
                               ops[i + 1].index == idx + len)
                        {
                            len += ops[i + 1].length;
                            i++;
                        }

                        result.Add(new DiffOp<T>(OperationType.Delete, idx, len));
                        continue;
                    }
                    // Merge Inserts/Adds
                    case OperationType.Insert:
                    case OperationType.Add:
                    {
                        var vals = DisposableList<T>.Create(op.values!);
                        int idx = op.index;
                        bool isAdd = op.type == OperationType.Add;

                        while (i + 1 < ops.Count &&
                               ops[i + 1].type == op.type &&
                               (isAdd || ops[i + 1].index == idx))
                        {
                            vals.AddRange(ops[i + 1].values!);
                            i++;
                        }
                        result.Add(new DiffOp<T>(op.type, idx, 0, vals));
                        continue;
                    }
                    default:
                        // Just copy others
                        result.Add(op);
                        break;
                }
            }

            return result;
        }

        public static void Apply<T>(DisposableList<T> list, DisposableList<DiffOp<T>> ops)
        {
            int offset = 0;
            int count = ops.Count;
            for (var i = 0; i < count; i++)
            {
                var op = ops[i];
                switch (op.type)
                {
                    case OperationType.Add:
                        list.AddRange(op.values);
                        offset += op.values.Count;
                        break;
                    case OperationType.Insert:
                        list.InsertRange(op.index + offset, op.values);
                        offset += op.values.Count;
                        break;

                    case OperationType.Delete:
                        list.RemoveRange(op.index + offset, op.length);
                        offset -= op.length;
                        break;
                    case OperationType.End:
                        break;
                    default: throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
