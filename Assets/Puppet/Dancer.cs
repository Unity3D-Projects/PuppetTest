﻿using UnityEngine;
using Klak.Math;

namespace Puppet
{
    public class Dancer : MonoBehaviour
    {
        #region Editable fields

        [SerializeField] float _stepFrequency = 2;
        [SerializeField] float _stride = 0.4f;
        [SerializeField] float _stepHeight = 0.3f;
        [SerializeField] float _stepAngle = 90;
        [SerializeField] float _bodyHeight = 0.9f;
        [SerializeField] float _bodyPositionNoise = 0.1f;
        [SerializeField] float _bodyRotationNoise = 30;
        [SerializeField] Vector3 _handPosition = new Vector3(0.3f, 0.3f, -0.2f);
        [SerializeField] float _handPositionNoise = 0.3f;
        [SerializeField] float _headMove = 3;
        [SerializeField] float _noiseFrequency = 1.1f;
        [SerializeField] int _randomSeed = 123;

        #endregion

        #region Private variables

        Animator _animator;

        XXHash _hash;
        NoiseGenerator _noise;

        // Foot positions
        Vector3[] _feet = new Vector3[2];

        // Timer for step animation
        float _step;

        // Transformation matrices of the chest bone. Used to calculate the
        // hand positions. These matrices are unavailable in OnAnimatorIK, so
        // cache them in Update.
        Matrix4x4 _chestMatrix, _chestMatrixInv;

        #endregion

        #region Local math functions

        static Vector3 SetY(Vector3 v, float y)
        {
            v.y = y;
            return v;
        }

        static float SmoothStep(float x)
        {
            return x * x * (3 - 2 * x);
        }

        #endregion

        #region Local properties and functions for foot animation

        // What number the current step is
        int StepCount { get { return Mathf.FloorToInt(_step); } }

        // Progress of the current step
        float StepTime { get { return _step - Mathf.Floor(_step); } }

        // Random seed for the current step
        int StepSeed { get { return StepCount * 100; } }

        // Is the pivot in the current step the left foot?
        bool PivotIsLeft { get { return (StepCount & 1) == 0; } }

        // Is the current step a left turn or a right turn?
        float StepSign { get {
            return _hash.Value01(StepSeed) > 0.5f ? 1 : -1;
        } }

        // Angle of the pivot rotation in the current step.
        float StepAngle { get {
            return _hash.Range(0.5f, 1.0f, StepSeed + 1) * _stepAngle * StepSign;
        } }

        // Pivot rotation at the end of the current step.
        Quaternion StepRotationFull { get {
            return Quaternion.AngleAxis(StepAngle, Vector3.up);
        } }

        // Pivot rotation at the current time.
        Quaternion StepRotation { get {
            return Quaternion.AngleAxis(StepAngle * StepTime, Vector3.up);
        } }

        // The original height of the foot point.
        Vector3 FootBias { get {
            return Vector3.up * _animator.leftFeetBottomHeight;
        } }

        // Calculates the foot position
        Vector3 GetFootPosition(int index)
        {
            var thisFoot = _feet[index];
            var thatFoot = _feet[(index + 1) & 1];

            // If it's the pivot foot, return it immediately.
            if (PivotIsLeft ^ (index == 1)) return thisFoot + FootBias;

            // Calculate the relative position from the pivot foot.
            var rp = StepRotation * (thisFoot - thatFoot);

            // Vertical move: Sine wave with smooth step
            var up = Mathf.Sin(SmoothStep(StepTime) * Mathf.PI) * _stepHeight;

            return thatFoot + rp + up * Vector3.up + FootBias;
        }

        Vector3 LeftFootPosition { get { return GetFootPosition(0); } }
        Vector3 RightFootPosition { get { return GetFootPosition(1); } }

        #endregion

        #region Local properties and functions for body animation

        // Body (hip) position
        Vector3 BodyPosition {
            get {
                // Move so that it keeps the center of mass on the pivot fott.
                var theta = (StepTime + (PivotIsLeft ? 0 : 1)) * Mathf.PI;
                var right = (1 - Mathf.Sin(theta)) / 2;
                var pos = Vector3.Lerp(LeftFootPosition, RightFootPosition, right);

                // Vertical move: Two wave while one step. Add noise.
                var y = _bodyHeight + Mathf.Cos(StepTime * Mathf.PI * 4) * _stepHeight / 2;
                y += _noise.Value(0) * _bodyPositionNoise;

                return SetY(pos, y);
            }
        }

        // Body (hip) rotation
        Quaternion BodyRotation {
            get {
                // Base rotation
                var rot = Quaternion.AngleAxis(-90, Vector3.up);

                // Right vector from the foot positions
                var right = SetY(RightFootPosition - LeftFootPosition, 0);

                // Horizontal rotation from the right vector.
                rot *= Quaternion.LookRotation(right.normalized);

                // Add noise.
                return rot * _noise.Rotation(1, _bodyRotationNoise);
            }
        }

        #endregion

        #region Local properties and functions for upper body animation

        // Calculates the hand position
        Vector3 GetHandPosition(int index)
        {
            var isLeft = (index == 0);

            // Relative position of the hand.
            var pos = _handPosition;
            if (isLeft) pos.x *= -1;

            // Apply the body (hip) transform.
            pos = _animator.bodyRotation * pos + _animator.bodyPosition;

            // Add noise.
            pos += _noise.Vector(2 + index) * _handPositionNoise;

            // Clamping in the local space of the chest bone.
            pos = _chestMatrixInv * new Vector4(pos.x, pos.y, pos.z, 1);
            pos.y = Mathf.Max(pos.y, 0.2f);
            pos.z = isLeft ? Mathf.Max(pos.z, 0.2f) : Mathf.Min(pos.z, -0.2f);
            pos = _chestMatrix * new Vector4(pos.x, pos.y, pos.z, 1);

            return pos;
        }

        Vector3 LeftHandPosition { get { return GetHandPosition(0); } }
        Vector3 RightHandPosition { get { return GetHandPosition(1); } }

        // Look at position (for head movement)
        Vector3 LookAtPosition {
            get {
                var pos = _noise.Vector(5) * _headMove;
                pos.z = 2;
                return _animator.bodyPosition + _animator.bodyRotation * pos;
            }
        }

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            _animator = GetComponent<Animator>();

            // Random number/noise generators
            _hash = new XXHash(_randomSeed);
            _noise = new NoiseGenerator(_noiseFrequency);

            // Initial foot positions
            var origin = SetY(transform.position, 0);
            var foot = transform.right * _stride / 2;
            _feet[0] = origin - foot;
            _feet[1] = origin + foot;
        }

        void Update()
        {
            // Noise update
            _noise.Frequency = _noiseFrequency;
            _noise.Step();

            // Check if the next step is going to begin in this frame.
            var delta = _stepFrequency * Time.deltaTime;

            if (StepCount < Mathf.FloorToInt(_step + delta))
            {
                // Update the next pivot point.
                var right = (_feet[1] - _feet[0]).normalized * _stride;
                if (PivotIsLeft)
                    _feet[1] = _feet[0] + StepRotationFull * right;
                else
                    _feet[0] = _feet[1] - StepRotationFull * right;
            }

            _step += delta;

            // Update the chest matrices for use in OnAnimatorIK.
            var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _chestMatrix = chest.localToWorldMatrix;
            _chestMatrixInv = chest.worldToLocalMatrix;
        }

        void OnAnimatorIK(int layerIndex)
        {
            _animator.SetIKPosition(AvatarIKGoal.LeftFoot, LeftFootPosition);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1);

            _animator.SetIKPosition(AvatarIKGoal.RightFoot, RightFootPosition);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1);

            _animator.bodyPosition = BodyPosition;
            _animator.bodyRotation = BodyRotation;

            var twist = Quaternion.AngleAxis(-8, Vector3.forward) * _noise.Rotation(5, 30, 20, 20);
            _animator.SetBoneLocalRotation(HumanBodyBones.Spine, twist);
            _animator.SetBoneLocalRotation(HumanBodyBones.Chest, twist);
            _animator.SetBoneLocalRotation(HumanBodyBones.UpperChest, twist);

            _animator.SetIKPosition(AvatarIKGoal.LeftHand, LeftHandPosition);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1);

            _animator.SetIKPosition(AvatarIKGoal.RightHand, RightHandPosition);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1);

            _animator.SetLookAtPosition(LookAtPosition);
            _animator.SetLookAtWeight(1);
        }

        #endregion
    }
}
