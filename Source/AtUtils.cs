// This code is based on Procedural Fairings plug-in by Alexey Volynskov, PMUtils class
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;


namespace AtHangar
{
	public class AtUtils
	{
		static bool haveTech (string name)
		{
			if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
				return name == "sandbox";
			return ResearchAndDevelopment.GetTechnologyState (name) == RDTech.State.Available;
		}
		
		public static float getTechMinValue (string cfgname, float defVal)
		{
			bool hasValue = false;
			float minVal = 0;
		
			foreach (var tech in GameDatabase.Instance.GetConfigNodes(cfgname))
				for (int i=0; i<tech.values.Count; ++i) {
					var value = tech.values [i];
					if (!haveTech (value.name))	continue;
					float v = float.Parse (value.value);
					if (!hasValue || v < minVal) {
						minVal = v;
						hasValue = true;
					}
				}
		
			if (!hasValue) return defVal;
			return minVal;
		}
		
		public static float getTechMaxValue (string cfgname, float defVal)
		{
			bool hasValue = false;
			float maxVal = 0;
		
			foreach (var tech in GameDatabase.Instance.GetConfigNodes(cfgname))
				for (int i=0; i<tech.values.Count; ++i) {
					var value = tech.values [i];
					if (!haveTech (value.name))	continue;
					float v = float.Parse (value.value);
					if (!hasValue || v > maxVal) {
						maxVal = v;
						hasValue = true;
					}
				}
		
			if (!hasValue) return defVal;
			return maxVal;
		}
		
		public static void setFieldRange (BaseField field, float minval, float maxval)
		{
			var fr = field.uiControlEditor as UI_FloatRange;
			if (fr != null) {
				fr.minValue = minval;
				fr.maxValue = maxval;
			}
		
			var fe = field.uiControlEditor as UI_FloatEdit;
			if (fe != null) {
				fe.minValue = minval;
				fe.maxValue = maxval;
			}
		}
		
		public static void updateAttachedPartPos (AttachNode node, Part part)
		{
			if (node == null || part == null) return;
		
			var ap = node.attachedPart;
			if (!ap) return;
		
			var an = ap.findAttachNodeByPart (part);
			if (an == null)	return;
		
			var dp =
				part.transform.TransformPoint (node.position) -
				ap.transform.TransformPoint (an.position);
		
			if (ap == part.parent) {
				while (ap.parent) ap = ap.parent;
				ap.transform.position += dp;
				part.transform.position -= dp;
			} else
				ap.transform.position += dp;
		}
		
		public static string formatMass (float mass)
		{
			if (mass < 0.01f)
				return (mass * 1e3f).ToString ("n3") + "kg";
			else
				return mass.ToString ("n3") + "t";
		}
	}
}
