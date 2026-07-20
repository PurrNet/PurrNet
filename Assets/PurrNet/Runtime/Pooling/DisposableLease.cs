using System.Collections.Generic;

namespace PurrNet.Pooling
{
    internal sealed class DisposableLease
    {
        public bool isAllocated;
        public int version;
    }

    internal static class DisposableLeasePool
    {
        private static readonly object _lock = new object();
        private static Stack<DisposableLease> _pool;

        private static Stack<DisposableLease> pool => _pool ??= new Stack<DisposableLease>();

        public static DisposableLease Rent(out int version)
        {
            lock (_lock)
            {
                var leases = pool;
                var lease = leases.Count > 0 ? leases.Pop() : new DisposableLease();
                unchecked
                {
                    lease.version++;
                    if (lease.version == 0)
                        lease.version = 1;
                }

                lease.isAllocated = true;
                version = lease.version;
                return lease;
            }
        }

        public static bool IsValid(DisposableLease lease, int version)
        {
            return lease != null && lease.isAllocated && lease.version == version;
        }

        public static void Return(DisposableLease lease, int version)
        {
            lock (_lock)
            {
                if (!IsValid(lease, version))
                    return;

                lease.isAllocated = false;
                pool.Push(lease);
            }
        }
    }
}
