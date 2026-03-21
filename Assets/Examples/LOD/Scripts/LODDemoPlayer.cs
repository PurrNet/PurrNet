using UnityEngine;

namespace PurrNet.Demo
{

    public class LODDemoPlayer : NetworkIdentity
    {
        
        [SerializeField] private float _speed = 8f;

        private void Update()
        {
            if (!isOwner) return;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            transform.position += new Vector3(h, 0f, v) * (_speed * Time.deltaTime);
        }
        
    }

}