using UnityEngine;
using System;
using System.Collections.Generic;

public class Cabinet : MonoBehaviour, IInteractable
{
    [Serializable]
    private class CabinetDoor
    {
        public Transform door;
        public Transform pivot;
        public Vector3 hingeAxis = Vector3.up;
        public float openAngle = 100f;
        public float openSpeed = 220f;

        [NonSerialized] public Quaternion closedPivotLocalRotation;
        [NonSerialized] public Vector3 closedDoorLocalPositionToPivot;
        [NonSerialized] public Quaternion closedDoorLocalRotationToPivot;
        [NonSerialized] public float currentAngle;
    }

    [Header("Cabinet Doors")]
    [SerializeField] private List<CabinetDoor> doors = new List<CabinetDoor>();
    [SerializeField] private bool startsOpen = false;

    private bool _isOpen;

    private void Awake()
    {
        if (doors.Count == 0)
        {
            doors.Add(new CabinetDoor { door = transform, pivot = transform });
        }

        _isOpen = startsOpen;

        for (int i = 0; i < doors.Count; i++)
        {
            CabinetDoor doorConfig = doors[i];
            if (doorConfig == null)
            {
                continue;
            }

            if (doorConfig.pivot == null)
            {
                doorConfig.pivot = doorConfig.door != null ? doorConfig.door : transform;
            }

            if (doorConfig.door == null)
            {
                doorConfig.door = doorConfig.pivot;
            }

            doorConfig.closedPivotLocalRotation = doorConfig.pivot.localRotation;
            Quaternion pivotRotationInverse = Quaternion.Inverse(doorConfig.pivot.rotation);
            doorConfig.closedDoorLocalPositionToPivot = pivotRotationInverse * (doorConfig.door.position - doorConfig.pivot.position);
            doorConfig.closedDoorLocalRotationToPivot = pivotRotationInverse * doorConfig.door.rotation;
            doorConfig.currentAngle = _isOpen ? doorConfig.openAngle : 0f;
            ApplyDoorRotation(doorConfig);
        }
    }

    private void Update()
    {
        for (int i = 0; i < doors.Count; i++)
        {
            CabinetDoor doorConfig = doors[i];
            if (doorConfig == null || doorConfig.pivot == null)
            {
                continue;
            }

            float targetAngle = _isOpen ? doorConfig.openAngle : 0f;
            doorConfig.currentAngle = Mathf.MoveTowards(doorConfig.currentAngle, targetAngle, doorConfig.openSpeed * Time.deltaTime);
            ApplyDoorRotation(doorConfig);
        }
    }

    public void Interact()
    {
        _isOpen = !_isOpen;
    }

    private static void ApplyDoorRotation(CabinetDoor doorConfig)
    {
        if (doorConfig.pivot == null || doorConfig.door == null)
        {
            return;
        }

        Vector3 axis = doorConfig.hingeAxis.sqrMagnitude > 0.0001f ? doorConfig.hingeAxis.normalized : Vector3.up;
        Quaternion hingeRotation = Quaternion.AngleAxis(doorConfig.currentAngle, axis);
        Quaternion targetPivotRotation = doorConfig.pivot.rotation * hingeRotation;
        if (doorConfig.door == doorConfig.pivot)
        {
            doorConfig.pivot.localRotation = doorConfig.closedPivotLocalRotation * hingeRotation;
            return;
        }

        doorConfig.door.rotation = targetPivotRotation * doorConfig.closedDoorLocalRotationToPivot;
        doorConfig.door.position = doorConfig.pivot.position + (targetPivotRotation * doorConfig.closedDoorLocalPositionToPivot);
    }
}
