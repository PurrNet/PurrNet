using PurrNet.Transports;

namespace PurrNet
{
    public interface IRegisterModules
    {
        void RegisterModules(ModulesCollection modules, bool asServer);

        bool isPromotingToServer { get; }

        bool isTranferingToNewServer { get; }

        ITransport currentTransport { get; }
    }
}
