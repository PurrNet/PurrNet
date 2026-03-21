using PurrNet.LOD;

namespace PurrNet.Demo
{

    [System.Serializable]
    public class DemoLODModule : DistanceLODModule
    {
        public LODDemoObject target;

        protected override void OnSendToObserver(PlayerID observer, float deltaTime)
        {
            if (target)
                target.TargetReceiveState(observer, deltaTime, target.counter);
        }
    }

}