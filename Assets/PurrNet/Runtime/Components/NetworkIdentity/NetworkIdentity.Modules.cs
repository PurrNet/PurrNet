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
            return true;
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
