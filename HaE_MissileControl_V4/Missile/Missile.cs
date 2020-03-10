using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public partial class Missile
        {
            // Error
            public MissileError Error { get; set; } = MissileError.None;
            public MissileStatus Idle { get; set; } = MissileStatus.Idle;

            // Control
            private List<IMyGyro> _gyros;
            private List<IMyThrust> _thrusters;

            // Detachment
            private IMyShipMergeBlock _mergeBlock;
            private IMyUserControllableGun _gun;

            // Payload
            private List<IMyWarhead> _warheads;
            private IMyTimerBlock _customP;

            // Detection
            private IMySensorBlock _sensor;

            // ProgramData
            private Vector3D _target;
            private Vector3D _previousPosition;
            private Vector3D _previousTargetPosition;
            private Vector3D _previousTargetSpeed;

            private bool _targetUpdated = false;

            private List<MyDetectedEntityInfo> _detectedEntities = new List<MyDetectedEntityInfo>();

            private readonly int _NavGain = 3;
            private readonly double _DetonateDistanceSq = 128;


            public Missile(
                List<IMyGyro> gyros, 
                List<IMyThrust> thrusters, 
                IMyShipMergeBlock mergeBlock = null, 
                IMyUserControllableGun gun = null, 
                List<IMyWarhead> warheads = null, 
                IMyTimerBlock customP = null, 
                IMySensorBlock sensor = null)
            {
                // Initialize
                _gyros = gyros;
                _thrusters = thrusters;
                _mergeBlock = mergeBlock;
                _gun = gun;
                _warheads = warheads;
                _customP = customP;
                _sensor = sensor;

                // Check if the missile is valid
                if (_gyros == null)
                    Error |= MissileError.MissingGyros;
                if (_thrusters == null)
                    Error |= MissileError.MissingThrust;
                if (_mergeBlock == null && gun == null && _warheads == null)
                    Error |= MissileError.MissingDetach;
                if (Error != MissileError.None)
                {
                    Error ^= MissileError.None;
                    return;
                }
            }

            public void UpdateTarget(Vector3D target)
            {
                _target = target;
            }

            public void Update()
            {
                if (!_targetUpdated)
                    _target = InterpolateTarget();

                Vector3D aimVector = Navigate(_target);
                GyroUtils.PointInDirection(_gyros, _gyros[0].WorldMatrix, aimVector);
                aimVector.Normalize();
                ThrustUtils.SetThrustBasedDot(_thrusters, aimVector);

                UpdatePayload();
            }

            private void UpdatePayload()
            {
                if (Vector3D.DistanceSquared(_target, _gyros[0].GetPosition()) < _DetonateDistanceSq)
                {
                    Detonate();
                    return;
                }

                if (_sensor != null)
                {
                    _sensor.DetectedEntities(_detectedEntities);
                    foreach (var entity in _detectedEntities)
                    {
                        if (entity.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies ||
                            entity.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral)
                        {
                            Detonate();
                        }
                    }
                }
            }
            
            private void Detonate()
            {
                _customP?.Trigger();

                if (_warheads == null)
                    return;

                foreach(var warhead in _warheads)
                {
                    warhead.IsArmed = true;
                    warhead.Detonate();
                }
            }

            private Vector3D InterpolateTarget()
            {
                Vector3D targetPosition = _previousPosition += _previousTargetSpeed;
                _previousPosition = targetPosition;

                return targetPosition;
            }

            private Vector3D Navigate(Vector3D targetpos)
            {
                Vector3D currentPos = _gyros[0].GetPosition();
                Vector3D myVel = currentPos - _previousPosition;
                Vector3D targetVel = targetpos - _previousTargetPosition;
                Vector3D myFThrust = ThrustUtils.GetThrustSum(_thrusters);

                Vector3D rangeVec = targetpos - _gyros[0].GetPosition();
                Vector3D closingVel = targetVel - myVel;

                Vector3D accel = CalculateAccel(rangeVec, closingVel);

                double accelMag = accel.Normalize();

                _previousTargetPosition = targetpos;
                _previousTargetSpeed = targetVel;
                _previousPosition = currentPos;

                accel *= accelMag;
                accel += rangeVec;
                return accel;
            }

            private Vector3D CalculateAccel(Vector3D rangeVec, Vector3D closingVelocity)
            {
                // Calculate rotation vec
                Vector3D RxV = Vector3D.Cross(rangeVec, closingVelocity);
                Vector3D RdR = rangeVec * rangeVec;
                Vector3D rotVec = RxV / RdR;

                Vector3D Los = Vector3D.Normalize(rangeVec);

                // Pronav term
                Vector3D accelerationNormal = (_NavGain * closingVelocity).Cross(rotVec);
                return accelerationNormal;
            }
        }
    }
}
