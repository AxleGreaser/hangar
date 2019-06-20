﻿//   HangarFairings.cs
//
//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2016 Allis Tauri

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI;
using KSP.UI.Screens;
using AT_Utils;

namespace AtHangar
{
    public class HangarFairings : Hangar, IPartCostModifier, IPartMassModifier, IMultipleDragCube
    {
        [KSPField] public string  Fairings          = "fairings";
        [KSPField] public float   FairingsDensity   = 0.5f; //t/m3
        [KSPField] public float   FairingsCost      = 20f;  //credits per fairing
        [KSPField] public Vector3 BaseCoMOffset     = Vector3.zero;
        [KSPField] public Vector3 JettisonDirection = Vector3.up;
        [KSPField] public float   JettisonForce     = 50f;
        [KSPField] public double  DebrisLifetime    = 600;
        [KSPField] public string  DecoupleNodes     = "";

        List<Transform> fairings = new List<Transform>();
        List<AttachNode> decoupleNodes = new List<AttachNode>();

        [KSPField(isPersistant = true)] public float debris_cost, debris_mass = -1f;

        [KSPField] public string  FxGroup = "decouple";
        FXGroup FX;

        [KSPField(isPersistant = true)]
        public int CrewCapacity = 0;

        [KSPField(isPersistant = true)]
        public bool jettisoned, launch_in_progress;
        List<Part> debris = new List<Part>();

        class PayloadRes : ConfigNodeObject
        { 
            [Persistent] public string name = ""; 
            [Persistent] public double amount = 0; 
            [Persistent] public double maxAmount = 0;

            public PayloadRes() {}
            public PayloadRes(PartResource res)
            {
                name = res.resourceName;
                amount = res.amount;
                maxAmount = res.maxAmount;
            }

            public void ApplyTo(PartResource res)
            {
                if(name == res.resourceName)
                {
                    res.amount = amount;
                    res.maxAmount = maxAmount;
                }
            }
        }
        readonly List<PayloadRes> payload_resources = new List<PayloadRes>();

        public override string GetInfo()
        {
            var info = base.GetInfo();
            if(LaunchVelocity != Vector3.zero)
                info += string.Format("Jettison Velocity: {0:F1}m/s\n", LaunchVelocity.magnitude);
            return info;
        }

        #region IPart*Modifiers
        public virtual float GetModuleCost(float defaultCost, ModifierStagingSituation situation) { return jettisoned ? -debris_cost : 0f; }
        public virtual ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }

        public virtual float GetModuleMass(float defaultMass, ModifierStagingSituation sit) { return jettisoned ? -debris_mass : 0f; }
        public virtual ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
        #endregion

        #region IMultipleDragCube
        static readonly string[] cube_names = {"Fairing", "Clean"};
        public string[] GetDragCubeNames() => cube_names;

        public void AssumeDragCubePosition(string anim) 
        {
            find_fairings();
            if(fairings.Count == 0) return;
            if(anim == "Fairing")
                fairings.ForEach(f => f.gameObject.SetActive(true));
            else 
                fairings.ForEach(f => f.gameObject.SetActive(false));
        }
        public bool UsesProceduralDragCubes() => false;
        public bool IsMultipleCubesActive => true;
        #endregion

        public void UpdateCoMOffset(Vector3 CoMOffset)
        {
            BaseCoMOffset = CoMOffset;
            if(jettisoned) part.CoMOffset = BaseCoMOffset;
        }

        void find_fairings()
        {
            fairings.Clear();
            foreach(var fairing in Utils.ParseLine(Fairings, Utils.Comma))
            {
                var transforms = part.FindModelTransforms(fairing);
                if(transforms != null) fairings.AddRange(transforms);
            }
        }

        protected override void early_setup(StartState state)
        {
            base.early_setup(state);
            NoGUI = true;
            LaunchWithPunch = true;
            update_crew_capacity(CrewCapacity);
            Events["EditName"].active = false;
            FX = part.findFxGroup(FxGroup);
            //setup fairings
            find_fairings();
            if(fairings.Count > 0)
            {
                if(jettisoned)
                {
                    fairings.ForEach(f => f.gameObject.SetActive(false));
                    part.DragCubes.SetCubeWeight("Fairing ", 0f);
                    part.DragCubes.SetCubeWeight("Clean ", 1f);
                    part.CoMOffset = BaseCoMOffset;
                    part.stagingIcon = string.Empty;
                    stagingToggleEnabledEditor = false;
                    stagingToggleEnabledFlight = false;
                    Events ["ToggleStaging"].advancedTweakable = true;
                    SetStaging(false);
                    part.UpdateStageability(true, true);
                }
                else
                {
                    fairings.ForEach(f => f.gameObject.SetActive(true));
                    part.DragCubes.SetCubeWeight("Fairing ", 1f);
                    part.DragCubes.SetCubeWeight("Clean ", 0f);
                    part.stagingIcon = "DECOUPLER_HOR";
                    stagingToggleEnabledEditor = true;
                    stagingToggleEnabledFlight = true;
                    Events ["ToggleStaging"].advancedTweakable = false;
                    SetStaging(true);
                    part.UpdateStageability(true, true);
                }
            }
            else this.Log("No Fairings transforms found with the name: {}", Fairings);
            //setup attach nodes
            decoupleNodes.Clear();
            foreach(var nodeID in Utils.ParseLine(DecoupleNodes, Utils.Comma))
            {
                var node = part.FindAttachNode(nodeID);
                if(node != null) decoupleNodes.Add(node);
            }
            JettisonDirection.Normalize();
            if(vessel != null) vessel.SpawnCrew();
            if(Storage != null)
            {
                Storage.OnVesselStored += on_ship_stored;
                Storage.OnVesselRemoved += on_ship_removed;
                Storage.OnStorageEmpty += on_storage_empty;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if(Storage != null)
            {
                Storage.OnVesselStored -= on_ship_stored;
                Storage.OnVesselRemoved -= on_ship_removed;
                Storage.OnStorageEmpty -= on_storage_empty;
            }
        }

        protected override bool try_store_vessel(PackedVessel v)
        {
            if(Storage.VesselsCount > 0)
            {
                Utils.Message("Payload is already stored");
                return false;
            }
            return base.try_store_vessel(v);
        }

        bool store_payload_resources(PackedVessel payload)
        {
            if(payload_resources.Count > 0) return false;
            var res_mass = 0.0;
            var resources = payload.resources;
            foreach(var r in resources.resourcesNames)
            {
                if(part.Resources.Contains(r)) continue;
                if(Globals.Instance.ResourcesBlacklist.IndexOf(r) >= 0) continue;
                var res = part.Resources.Add(r, resources.ResourceAmount(r), resources.ResourceCapacity(r), 
                                             true, true, true, true, PartResource.FlowMode.Both);
                if(res != null) 
                {
                    payload_resources.Add(new PayloadRes(res));
                    resources.TransferResource(r, -res.amount);
                    res_mass += res.amount*res.info.density;
                }
            }
            payload.mass -= (float)res_mass;
            Storage.UpdateParams();
            return true;
        }

        public void ResetPayloadResources()
        {
            if(payload_resources.Count == 0) return;
            foreach(var r in payload_resources)
            {
                var res = part.Resources.Get(r.name);
                if(res != null) r.ApplyTo(res);
            }
        }

        bool restore_payload_resources(PackedVessel payload)
        {
            if(payload_resources.Count == 0) return true;
            if(HighLogic.LoadedSceneIsEditor)
                ResetPayloadResources();
            var res_mass = 0.0;
            foreach(var r in payload_resources)
            {
                var res  = part.Resources.Get(r.name);
                if(res != null)
                {
                    res_mass += res.amount * res.info.density;
                    payload.resources.TransferResource(r.name, res.amount);
                    part.Resources.Remove(res);
                }
            }
            payload.mass += (float)res_mass;
            payload_resources.Clear();
            Storage.UpdateParams();
            return true;
        }

        bool clear_payload_resouces()
        {
            if(payload_resources.Count == 0) return true;
            if(Storage != null && Storage.Ready && Storage.VesselsCount > 0) return false;
            payload_resources.ForEach(r => part.Resources.Remove(r.name));
            payload_resources.Clear();
            return true;
        }

        void on_ship_stored(PackedVessel pc)
        {
            update_crew_capacity(pc.CrewCapacity);
            store_payload_resources(pc);
        }

        void on_ship_removed(PackedVessel pc)
        {
            if(HighLogic.LoadedSceneIsEditor)
                update_crew_capacity(0);
            restore_payload_resources(pc);
        }

        void on_storage_empty()
        {
            if(HighLogic.LoadedSceneIsEditor)
                update_crew_capacity(0);
            clear_payload_resouces();
        }

        void update_crew_capacity(int capacity)
        {
            part.CrewCapacity = CrewCapacity = capacity;
            if(part.partInfo != null && part.partInfo.partPrefab != null)
                part.partInfo.partPrefab.CrewCapacity = part.CrewCapacity;
            if(HighLogic.LoadedSceneIsEditor)
            {
                ShipConstruction.ShipConfig = EditorLogic.fetch.ship.SaveShip();
                ShipConstruction.ShipManifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(ShipConstruction.ShipConfig);
                if(CrewAssignmentDialog.Instance != null)
                    CrewAssignmentDialog.Instance.RefreshCrewLists(ShipConstruction.ShipManifest, false, true);
                Utils.UpdateEditorGUI();
            }
        }

        struct ForceTarget
        { 
            public Vector3 pos;
            public Vector3 force;
            public Rigidbody target;
            public ForceTarget(Rigidbody target, Vector3 force, Vector3 pos)
            {
                this.target = target; 
                this.force = force;
                this.pos = pos;
            }

            public void Apply(Rigidbody counterpart)
            {
                target.AddForceAtPosition(force, pos, ForceMode.Force);
                counterpart.AddForceAtPosition(-force, pos, ForceMode.Force);
            }
        }

        protected override IEnumerable<YieldInstruction> before_vessel_launch(PackedVessel vsl)
        {
            if(fairings.Count == 0 || jettisoned) yield break;
            //store crew
            vsl.crew.Clear();
            vsl.crew.AddRange(part.protoModuleCrew);
            //decouple surface attached parts and decoupleNodes
            var decouple = new List<Part>();
            foreach(var p in part.children)
            {
                if(p.srfAttachNode != null && 
                   p.srfAttachNode.attachedPart == part)
                    decouple.Add(p);
            }
            foreach(var node in decoupleNodes)
            {
                if(node.attachedPart != null)
                {
                    if(node.attachedPart == part.parent)
                        decouple.Add(part);
                    else decouple.Add(node.attachedPart);
                }
            }
            var jettison = new List<ForceTarget>(decouple.Count);
            foreach(var p in decouple)
            {
                var force_target = p;
                if(p == part) 
                    force_target = part.parent;
                p.decouple();
                if(force_target.Rigidbody != null) 
                {
                    var pos = force_target.Rigidbody.worldCenterOfMass;
                    var force = (pos-part.Rigidbody.worldCenterOfMass).normalized*force_target.mass*JettisonForce*0.5f;
                    jettison.Add(new ForceTarget(force_target.Rigidbody, force, pos));
                }
                yield return null;
            }
            //spawn debirs
            debris = new List<Part>();
            debris_cost = 0;
            foreach(var f in fairings)
            {
                var d = Debris.SetupOnTransform(part, f, FairingsDensity, FairingsCost, DebrisLifetime);
                var force = f.TransformDirection(JettisonDirection) * JettisonForce * 0.5f;
                var pos = d.Rigidbody.worldCenterOfMass;
                jettison.Add(new ForceTarget(d.Rigidbody, force, pos));
                d.SetDetectCollisions(false);
                d.vessel.IgnoreGForces(10);
                debris_cost += FairingsCost;
                debris.Add(d);
            }
            //apply force to spawned/decoupled objects
            jettison.ForEach(j => j.Apply(part.Rigidbody));
            //update drag cubes
            part.DragCubes.SetCubeWeight("Fairing ", 0f);
            part.DragCubes.SetCubeWeight("Clean ", 1f);
            part.DragCubes.ForceUpdate(true, true, true);
            //this event is catched by FlightLogger
            StartCoroutine(CallbackUtil.DelayedCallback(5, update_debris_after_launch));
            GameEvents.onStageSeparation.Fire(new EventReport(FlightEvents.STAGESEPARATION, part, null, null, StageManager.CurrentStage, string.Empty));
            if(FX != null) FX.Burst();
            jettisoned = true;
        }

        void update_debris_after_launch()
        {
            debris_mass = 0;
            debris.ForEach(p =>
            {
                if(p != null && p.Rigidbody != null)
                {
                    debris_mass += p.Rigidbody.mass;
                    p.SetDetectCollisions(true);
                }
            });
            debris.Clear();
        }

        protected override void on_vessel_loaded(Vessel vsl)
        {
            base.on_vessel_loaded(vsl);
            //transfer the target and controls
            vsl.protoVessel.targetInfo = vessel.BackupVessel().targetInfo;
            vsl.ResumeTarget();
            vsl.ctrlState.CopyFrom(vessel.ctrlState);
            vsl.ActionGroups.CopyFrom(vessel.ActionGroups);
        }

        protected override void on_vessel_off_rails(Vessel vsl)
        {
            base.on_vessel_off_rails(vsl);
            //transfer the flight plan
            this.Log("patch manager: {}, nodes {}", vessel.patchedConicSolver, vessel.patchedConicSolver?.maneuverNodes);//debug
            if(vessel.patchedConicSolver != null &&
                vessel.patchedConicSolver.maneuverNodes.Count > 0)
            {
                var nearest_node = vessel.patchedConicSolver.maneuverNodes[0];
                this.Log("node dV 0: {}", nearest_node.DeltaV);//debug
                var o = vsl.orbit;
                var norm = o.GetOrbitNormal().normalized;
                var prograde = o.getOrbitalVelocityAtUT(nearest_node.UT);
                var radial = Vector3d.Cross(prograde, norm).normalized;
                var orbitalDeltaV = nearest_node.nextPatch.getOrbitalVelocityAtUT(nearest_node.UT) - prograde;
                prograde.Normalize();
                nearest_node.DeltaV = new Vector3d(Vector3d.Dot(orbitalDeltaV, radial),
                                        Vector3d.Dot(orbitalDeltaV, norm),
                                        Vector3d.Dot(orbitalDeltaV, prograde));
                this.Log("node dV 1: {}", nearest_node.DeltaV);//debug
                //var vvel = o.getOrbitalVelocityAtUT(nearest_node.UT).xzy;
                //var vpos = o.getPositionAtUT(nearest_node.UT).xzy;
                //nearest_node.nodeRotation = Quaternion.LookRotation(vvel, Vector3d.Cross(-vpos, vvel));
                //nearest_node.DeltaV = nearest_node.nodeRotation.Inverse() * (nearest_node.nextPatch.getOrbitalVelocityAtUT(nearest_node.UT).xzy - vvel);
                vsl.flightPlanNode.ClearData();
                vessel.patchedConicSolver.Save(vsl.flightPlanNode);
                vsl.patchedConicSolver.Load(vsl.flightPlanNode);
                //clear this vessel's flight plan
                vessel.patchedConicSolver.maneuverNodes.Clear();
                vessel.patchedConicSolver.flightPlan.Clear();
                vessel.flightPlanNode.ClearData();
            }
        }

        protected override void on_vessel_launched(Vessel vsl)
        {
            //turn everything off
            vessel.ctrlState.Neutralize();
            if(vessel == FlightGlobals.fetch?.activeVessel)
                FlightInputHandler.SetNeutralControls();
            Storage.EnableModule(false);
            Events["LaunchVessel"].active = Actions["LaunchVesselAction"].active = false;
            //update CoM and crew capacity
            part.CoMOffset = BaseCoMOffset;
            update_crew_capacity(0);
            base.on_vessel_launched(vsl);
        }

        IEnumerator<YieldInstruction> delayed_launch()
        {
            //check state
            if(!HighLogic.LoadedSceneIsFlight || Storage == null || !Storage.Ready) yield break;
            if(Storage.VesselsCount == 0) 
            {
                Utils.Message("No payload");
                yield break;
            }
            if(gates_state != AnimatorState.Opened && 
               hangar_gates != null && !hangar_gates.Playing) yield break;
            //set the flag and wait for the doors to open
            launch_in_progress = true;
            if(hangar_gates != null)
                while(hangar_gates.Playing) 
                    yield return null;
            //activate the hangar, get the vessel from the storage, set its crew
            Activate();
            //try to restore vessel and check the result
            if(!TryRestoreVessel(Storage.GetVessels()[0]))
                //if jettisoning has failed, deactivate the part
                part.deactivate();
            //otherwise on resume the part is activated automatically
            launch_in_progress = false;
        }

        [KSPEvent(guiActive = true, guiName = "Jettison Payload", guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 4)]
        public void LaunchVessel()
        {
            if(launch_in_progress) return;
            Open();    
            StartCoroutine(delayed_launch());
        }

        [KSPAction("Jettison Payload")]
        public void LaunchVesselAction(KSPActionParam param) => LaunchVessel();

        public override void OnActive()
        { 
            if(HighLogic.LoadedSceneIsFlight)
                LaunchVessel(); 
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            var payload_node = node.AddNode("PAYLOAD_RESOURCES");
            payload_resources.ForEach(r => r.Save(payload_node.AddNode("RESOURCE")));
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            var payload_node = node.GetNode("PAYLOAD_RESOURCES");
            if(payload_node != null)
            {
                foreach(var rn in payload_node.GetNodes("RESOURCE"))
                {
                    var res = ConfigNodeObject.FromConfig<PayloadRes>(rn);
                    if(res != null) payload_resources.Add(res);
                }
            }
        }

        #if DEBUG
//        Vector3d last_pos = Vector3d.zero;
//        Vector3d last_opos = Vector3d.zero;
//        public override void FixedUpdate()
//        {
//            base.FixedUpdate();
//            if(debris != null && debris.Count > 0)
//            {
//                var d = debris[0];
//                var delta = (d.vessel.CoM-last_pos).magnitude;
//                var odelta = (d.orbit.pos-last_opos).magnitude;
//                this.Log("delta pos:  {}\n" +
//                         "delta orb:  {}\n" +
//                         "pos-CB - orb: {}\n" +
//                         "orbit:\n{}\n" +
//                         "driver.offsetByFrame {}, was {}\n" +
//                         "driver.localCoM {}", 
//                         delta.ToString("F3"), 
//                         odelta.ToString("F3"),
//                         (d.vessel.CoMD-d.vessel.mainBody.position).xzy-d.orbit.pos,
//                         d.orbit, d.vessel.orbitDriver.offsetPosByAFrame, d.vessel.orbitDriver.wasOffsetPosByAFrame,
//                         d.vessel.orbitDriver.localCoM);
//                last_pos = d.vessel.CoM;
//                last_opos = d.orbit.pos;
//            }
//        }
        #endif
    }

    public class HangarFairingsUpdater : ModuleUpdater<HangarFairings>
    {
        protected override void on_rescale(ModulePair<HangarFairings> mp, Scale scale)
        { 
            mp.module.JettisonForce = mp.base_module.JettisonForce * scale.absolute.volume;
            mp.module.FairingsCost  = mp.base_module.FairingsCost * scale.absolute.volume;
            mp.module.UpdateCoMOffset(scale.ScaleVector(mp.base_module.BaseCoMOffset));
            if(HighLogic.LoadedSceneIsEditor) mp.module.ResetPayloadResources();
        }
    }
}

