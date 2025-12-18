using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(menuName = "PurrNet/NetworkVisibility/Rule Set", fileName = "New Rule Set")]
    public class NetworkVisibilityRuleSet : ScriptableObject
    {
        [Tooltip("If true, children will inherit this rule set unless they have their own overrides")] [SerializeField]
        private bool _childrenInherit = true;

        [SerializeField] private NetworkVisibilityRule[] _rules;

        public bool childrenInherit => _childrenInherit;

        private readonly List<INetworkVisibilityRule> _raw_rules = new List<INetworkVisibilityRule>();

        public bool isInitialized { get; private set; }

        public void Setup(NetworkManager manager)
        {
            if (isInitialized)
                return;

            isInitialized = true;

            for (int i = 0; i < _rules.Length; i++)
            {
                _rules[i].Setup(manager);
                _raw_rules.Add(_rules[i]);
            }

            _raw_rules.Sort((a, b) =>
                a.complexity.CompareTo(b.complexity));
        }

        public void AddRule(NetworkManager manager, INetworkVisibilityRule rule)
        {
            if (rule is NetworkVisibilityRule nrule)
                nrule.Setup(manager);

            // insert the rule in the correct order
            for (int i = 0; i < _raw_rules.Count; i++)
            {
                if (_raw_rules[i].complexity > rule.complexity)
                {
                    _raw_rules.Insert(i, rule);
                    return;
                }
            }
        }

        public void RemoveRule(INetworkVisibilityRule rule)
        {
            _raw_rules.Remove(rule);
        }

        public bool CanSee(PlayerID player, NetworkIdentity target)
        {
            var rules = target.networkRules;
            if (rules && rules.ShouldForceVisibilityToAlwaysVisible())
                return true;

            for (int i = 0; i < _raw_rules.Count; i++)
            {
                if (_raw_rules[i].CanSee(player, target))
                    return true;
            }

            return false;
        }
    }
}
