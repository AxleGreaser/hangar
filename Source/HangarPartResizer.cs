// This code is based on Procedural Fairings plug-in by Alexey Volynskov, KzPartResizer class
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace AtHangar
{
	public class HangarPartResizer : PartModule
	{
		[KSPField(isPersistant=true, guiActiveEditor=true, guiName="Scale", guiFormat="S4")]
		[UI_FloatEdit(scene=UI_Scene.Editor, minValue=0.1f, maxValue=10, incrementLarge=1.25f, incrementSmall=0.125f, incrementSlide=0.001f)]
		public float size = 1.0f;
		
		[KSPField(isPersistant=true, guiActiveEditor=true, guiName="Length", guiFormat="S4")]
		[UI_FloatEdit(scene=UI_Scene.Editor, minValue=0.1f, maxValue=10, incrementLarge=1.0f, incrementSmall=0.1f, incrementSlide=0.001f)]
		public float length = 1.0f;
		
		[KSPField] public bool lengthOnly = false;
		
		[KSPField] public float sizeStepLarge = 1.25f;
		[KSPField] public float sizeStepSmall = 0.125f;
		
		[KSPField] public float lengthStepLarge = 1.0f;
		[KSPField] public float lengthStepSmall = 0.1f;
		
		[KSPField] public Vector4 specificMass = new Vector4(0.005f, 0.011f, 0.009f, 0f);
		[KSPField] public float specificBreakingForce  = 1536;
		[KSPField] public float specificBreakingTorque = 1536;
		
		[KSPField] public string minSizeName = "HANGAR_MINSCALE";
		[KSPField] public string maxSizeName = "HANGAR_MAXSCALE";
		
		[KSPField(isPersistant=false, guiActive=false, guiActiveEditor=true, guiName="Mass")]
		public string massDisplay;
		
		protected float old_size   = -1000;
		protected float old_length = -1000;
		protected bool just_loaded = false;
		
		protected int orig_top_size;
		protected int orig_bottom_size;
		protected int orig_docking_size;
		
		
		private Vector3 scale_vector(Vector3 v, float s, float l)
		{ return Vector3.Scale(v, new Vector3(s, s*l, s)); }
		
		public override void OnStart (StartState state)
		{
			base.OnStart (state);
			if (HighLogic.LoadedSceneIsEditor) 
			{
				float minSize = Utils.getTechMinValue (minSizeName, 0.1f);
				float maxSize = Utils.getTechMaxValue (maxSizeName, 10);
				
				if(!lengthOnly)
				{
					Utils.setFieldRange (Fields ["size"], minSize, maxSize);
					((UI_FloatEdit)Fields ["size"].uiControlEditor).incrementLarge = sizeStepLarge;
					((UI_FloatEdit)Fields ["size"].uiControlEditor).incrementSmall = sizeStepSmall;
				}
				else Fields["size"].guiActiveEditor=false;
				
				Utils.setFieldRange (Fields ["length"], minSize, maxSize);
				((UI_FloatEdit)Fields ["length"].uiControlEditor).incrementLarge = lengthStepLarge;
				((UI_FloatEdit)Fields ["length"].uiControlEditor).incrementSmall = lengthStepSmall;
			}
			part.force_activate();
			orig_top_size = part.findAttachNode("top").size;
			orig_bottom_size = part.findAttachNode("bottom").size;
			orig_docking_size = part.findAttachNode("docking").size;
			updateNodeSizes(size);
		}
		
		public override void OnLoad (ConfigNode cfg)
		{
			base.OnLoad(cfg);
			just_loaded = true;
			if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
				updateNodeSizes(size);
		}
		
		public virtual void FixedUpdate ()
		{
			if (size != old_size || length != old_length) 
				resizePart(size, length);
			just_loaded = false;
		}
		
		public void scaleNode(AttachNode node, float scale, float len)
		{
			if(node == null) return;
			node.position = scale_vector(node.originalPosition, scale, len);
			if (!just_loaded)
				Utils.updateAttachedPartPos(node, part);
		}
		
		public void setNodeSize(AttachNode node, float scale, int orig_size)
		{
			if (node == null) return;
			int new_size = orig_size + Mathf.RoundToInt(scale/sizeStepLarge) - 1;
			if(new_size < 0) new_size = 0;
			node.size = new_size;
		}
		
		public virtual void updateDockingNode()
		{
			ModuleDockingNode dock = part.Modules.OfType<ModuleDockingNode>().SingleOrDefault();
			if(dock == null) return;
			AttachNode node = part.findAttachNode(dock.referenceAttachNode);
			if(node == null) return;
			dock.nodeType = string.Format("size{0}", node.size);
		}
		
		public virtual void updateNodeSizes(float scale)
		{
			setNodeSize(part.findAttachNode("top"), scale, orig_top_size);
			setNodeSize(part.findAttachNode("bottom"), scale, orig_bottom_size);
			setNodeSize(part.findAttachNode("docking"), scale, orig_docking_size);
			updateDockingNode();
		}
		
		public void scaleNodes(float scale, float len)
		{
			scaleNode(part.findAttachNode("top"), scale, len);
			scaleNode(part.findAttachNode("bottom"), scale, len);
			scaleNode(part.findAttachNode("docking"), scale, len);
		}

		
		public virtual void resizePart (float scale, float len)
		{
			old_size   = size;
			old_length = length;
		
			//change mass and forces
			part.mass  = ((specificMass.x * scale + specificMass.y) * scale + specificMass.z) * scale + specificMass.w;
			part.mass *= len;
			massDisplay = Utils.formatMass (part.mass);
			part.breakingForce = specificBreakingForce * Mathf.Pow (scale, 2);
			part.breakingTorque = specificBreakingTorque * Mathf.Pow (scale, 2);
			
			//change scale
			Transform model = part.FindModelTransform ("model");
			if(model != null)
				model.localScale = scale_vector(Vector3.one, scale, len);
			else
				Debug.LogError ("[HangarPartResizer] No 'model' transform in the part", this);
			
			//change volume if the part is a hangar
			Hangar hangar = part.Modules.OfType<Hangar>().SingleOrDefault();
			if(hangar != null) hangar.Setup();
		
			scaleNodes(scale, len);
			updateNodeSizes(scale);
		}
	}
}

