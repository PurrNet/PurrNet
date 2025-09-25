using LiteNetLib;
using System.Net;

namespace PurrNet.Transports
{
  public class PeerInfo
  {
    public IPAddress Address { get; set; }

    public int Port { get; set; }

    public int Id { get; set; }

    /// <summary>
    /// Creates a new PeerInfo populated with Address, Port, and Id copied from the provided NetPeer.
    /// </summary>
    /// <param name="fromNetPeer">The source NetPeer whose Address, Port, and Id will be copied.</param>
    /// <returns>A PeerInfo instance with Address, Port, and Id taken from <paramref name="fromNetPeer"/>.</returns>
    static public PeerInfo Generate(NetPeer fromNetPeer)
    {
      return new PeerInfo()
      {
        Address = fromNetPeer.Address,
        Port = fromNetPeer.Port,
        Id = fromNetPeer.Id
      };
    }
  }
}