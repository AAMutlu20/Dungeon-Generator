using UnityEngine;

namespace Player
{
    public class SmoothFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float smoothSpeed = 0.125f;

        private Vector3 _offset;

        private void Start()
        {
            if (target == null)
            {
                Debug.LogError("SmoothFollowCamera: no target assigned.");
                return;
            }
            _offset = transform.position - target.position;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + _offset;
            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed);
        }
    }
}