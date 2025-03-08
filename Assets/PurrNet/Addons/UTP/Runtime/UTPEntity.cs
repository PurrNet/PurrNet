using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

#if UTP_TRANSPORT
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
#endif
#if UTP_RELAY
using Unity.Services.Relay.Models;
#endif

namespace PurrNet.UTP {
    /// <summary>
    /// A server or client inside Utp.
    /// </summary>
    public abstract class UTPEntity {
#if UTP_TRANSPORT
        /// <summary>
        /// Temporary storage for connection events that occur on job threads so they may be dequeued on the main thread.
        /// </summary>
        protected NativeQueue<UTPConnectionEvent> connectionsEventsQueue;

        /// <summary>
        /// Used alongside a connection to connect, send, and receive data from a listen server.
        /// </summary>
        protected NetworkDriver driver;

        /// <summary>
		/// A pipeline on the driver that is sequenced, and ensures messages are delivered.
		/// </summary>
		protected NetworkPipeline reliablePipeline;

        /// <summary>
        /// A pipeline on the driver that is sequenced, but does not ensure messages are delivered.
        /// </summary>
        protected NetworkPipeline unreliablePipeline;

        /// <summary>
		/// Job handle to schedule jobs.
		/// </summary>
		protected JobHandle jobHandle;

        /// <summary>
        /// Returns whether a connection is a valid one. Checks against default connection object.
        /// </summary>
        /// <param name="connection">The connection to validate.</param>
        /// <returns>True or false, whether the connection is valid.</returns>
        public bool IsValidConnection(NetworkConnection connection) {
            return connection.IsCreated;
        }
#endif

        /// <summary>
        /// Determine whether the NetworkDriver has been initialized.
        /// </summary>
        /// <returns>True if initialized, false otherwise.</returns>
        public bool IsNetworkDriverInitialized() {
#if UTP_TRANSPORT
            return driver.IsCreated;
#else
            return false;
#endif
        }

#if UTP_RELAY
        /// <summary>
        /// Gets a network type and returns its string alternative.
        /// </summary>
        /// <param name="connectionType">The type of connection to stringify.</param>
        /// <returns>The connection type as a string.</returns>
        protected string GetStringFromConnectionType(RelayServerEndpoint.NetworkOptions connectionType) {
            switch(connectionType) {
                case (RelayServerEndpoint.NetworkOptions.Tcp): return "tcp";
                case (RelayServerEndpoint.NetworkOptions.Udp): return "udp";
                default: return string.Empty;
            }
        }

        protected RelayAllocationId ConvertFromAllocationIdBytes(byte[] allocationIdBytes) {
            unsafe {
                fixed(byte* ptr = allocationIdBytes) {
                    return RelayAllocationId.FromBytePointer(ptr, allocationIdBytes.Length);
                }
            }
        }

        protected RelayConnectionData ConvertConnectionData(byte[] connectionData) {
            unsafe {
                fixed(byte* ptr = connectionData) {
                    return RelayConnectionData.FromBytePointer(ptr, RelayConnectionData.k_Length);
                }
            }
        }

        protected RelayHMACKey ConvertFromHMAC(byte[] hmac) {
            unsafe {
                fixed(byte* ptr = hmac) {
                    return RelayHMACKey.FromBytePointer(ptr, RelayHMACKey.k_Length);
                }
            }
        }

        protected RelayServerEndpoint GetEndpointForConnectionType(List<RelayServerEndpoint> endpoints, string connectionType) {
            foreach(var endpoint in endpoints) {
                if(endpoint.ConnectionType == connectionType) {
                    return endpoint;
                }
            }

            return null;
        }
#endif
    }
}
