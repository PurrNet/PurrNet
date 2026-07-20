namespace PurrNet.Modules
{
    public class ColliderRollbackFactory : SceneScopedFactory<RollbackModule>, IPostFixedUpdate
    {
        readonly TickManager _tick;
        readonly NetworkManager _manager;

        public ColliderRollbackFactory(NetworkManager manager, TickManager tick, ScenesModule scenes)
            : base(scenes)
        {
            _manager = manager;
            _tick = tick;
        }

        protected override RollbackModule CreateModule(SceneID scene, bool asServer)
        {
            if (!asServer &&
                _manager.TryGetModule<ColliderRollbackFactory>(true, out var serverFactory) &&
                serverFactory.TryGetModule(scene, out var serverModule))
                return serverModule;

            if (scenes.TryGetSceneState(scene, out var state))
                return new RollbackModule(_tick, state.scene);
            return new RollbackModule(_tick, default);
        }

        public void PostFixedUpdate()
        {
            foreach (var module in modules)
                module.OnPostTick();
        }
    }
}
