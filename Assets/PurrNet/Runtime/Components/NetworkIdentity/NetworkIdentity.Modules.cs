using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkIdentity
    {
        public IReadOnlyList<NetworkModule> modules => _externalModulesView;

        private readonly List<NetworkModule> _externalModulesView = new List<NetworkModule>();
        private readonly List<NetworkModule> _modules = new List<NetworkModule>();
        private Dictionary<int, string> _nullModules;

        private byte _moduleId;

        [UsedByIL]
        public void RegisterModuleInternal(string moduleName, string type, NetworkModule module, bool isNetworkIdentity)
        {
            if (_moduleId >= byte.MaxValue)
            {
                throw new System.Exception($"Too many modules in {GetType().Name}! Max is {byte.MaxValue}.\n" +
                                           $"This could also happen with circular dependencies.");
            }

            if (module == null)
            {
                (_nullModules ??= new Dictionary<int, string>())[_moduleId] = $"{type} {moduleName}";
                ++_moduleId;
                _modules.Add(null);

                if (isNetworkIdentity)
                {
                    PurrLogger.LogWarning($"Module in {GetType().Name} is null: <i>{type}</i> {moduleName};\n" +
                                          $"You can initialize it on Awake or override OnInitializeModules.",
                        this);
                }

                return;
            }

            module.SetComponentParent(this, _moduleId++, moduleName);

            _modules.Add(module);
            _externalModulesView.Add(module);
        }

        public bool TryGetModule(int moduleId, out NetworkModule module)
        {
            if (moduleId >= _modules.Count)
            {
                module = null;
                return false;
            }

            module = _modules[moduleId];
            return module != null;
        }

        internal string DescribeMissingModule(int moduleId, bool asServer)
        {
            var side = asServer ? "server" : "client";

            if (_nullModules != null && _nullModules.TryGetValue(moduleId, out var nullModule))
            {
                return $"Module <i>{nullModule}</i> (id {moduleId}) on '{name}' ({GetType().Name}) is null on the {side}, " +
                       "but the other side has it and sent it an RPC. Network modules must exist on both sides before spawn: " +
                       "initialize it in its field declaration, in Awake, or by overriding OnInitializeModules.";
            }

            return $"Module id {moduleId} doesn't exist on '{name}' ({GetType().Name}) on the {side}, which only has {_modules.Count} modules. " +
                   "The server and client registered different modules for this object; make sure both sides use the same prefab and scripts.";
        }

        private void RegisterEvents()
        {
            for (var i = 0; i < _externalModulesView.Count; i++)
            {
                var module = _externalModulesView[i];
                if (module is ITick tickableModule)
                {
                    _tickables.Add(tickableModule);
                }
            }
        }
    }
}
