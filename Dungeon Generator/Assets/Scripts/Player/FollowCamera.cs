using UnityEngine;

namespace Player
{
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float smoothSpeed = 0.125f;

        private Vector3 _offset;

        private void Start()
        {
            if (!target)
            {
                Debug.LogError("Nope. No target assigned");
                return;
            }
            _offset = transform.position - target.position;
        }

        private void LateUpdate()
        {
            if (!target) return;
            var desired = target.position + _offset;
            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed);
        }
    }
}