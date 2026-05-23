using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;          // ← new namespace

public class MouseClickController : MonoBehaviour
{
    public Vector3 clickPosition;
    public UnityEvent<Vector3> OnClick;

    private Ray _lastRay;
    private Camera _camera;

    void Start() => _camera = Camera.main;

    void Update()
    {
        // ↓ new Input System equivalent of Input.GetMouseButtonDown(0)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            _lastRay = _camera.ScreenPointToRay(mousePos);

            if (Physics.Raycast(_lastRay, out RaycastHit hit))
            {
                clickPosition = hit.point;
                OnClick?.Invoke(clickPosition);
            }
        }

        Debug.DrawRay(_lastRay.origin, _lastRay.direction * 100f, Color.red);
    }
}