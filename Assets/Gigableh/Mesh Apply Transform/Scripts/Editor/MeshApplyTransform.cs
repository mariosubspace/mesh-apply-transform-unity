using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Gigableh
{
	public class MeshApplyTransform : EditorWindow
	{
		bool applyTranslation = false;
		bool applyRotation = false;
		bool applyScale = false;

		[MenuItem("Mesh/Apply Transform")]
		static void Init()
		{
			if (Selection.activeGameObject == null)
			{
				Debug.LogError("MeshApplyTransform:: No selected object.");
				return;
			}

			MeshApplyTransform window = (MeshApplyTransform)EditorWindow.GetWindow(typeof(MeshApplyTransform));
			
			int width = 200;
			int height = 150;

			var main = CustomEditorExtensions.GetEditorMainWindowPos();
			float xOff = (main.width - width) * 0.5f;
			float yOff = (main.height - height) * 0.5f;
			float x = main.x + xOff;
			float y = main.y + yOff;
			window.position = new Rect(x, y, width, height);

			window.Show();
		}

		void OnEnable()
		{
			titleContent = new GUIContent("Apply Transform");
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField("Properties to Apply", EditorStyles.boldLabel);
			applyTranslation = EditorGUILayout.Toggle("Position", applyTranslation);
			applyRotation = EditorGUILayout.Toggle("Rotation", applyRotation);
			applyScale = EditorGUILayout.Toggle("Scale", applyScale);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Method", EditorStyles.boldLabel);
			if (GUILayout.Button("Apply Individual"))
			{
				ApplyTransform(Selection.GetTransforms(SelectionMode.ExcludePrefab), applyTranslation, applyRotation, applyScale);
				this.Close();
			}
			if (GUILayout.Button("Apply Recursively"))
			{
				ApplyTransformRecursive(Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.ExcludePrefab), applyTranslation, applyRotation, applyScale);
				this.Close();
			}
		}

		// Apply transform recursively for multiple parents, the list of transforms
		// should not include any children of another item in the list. They should
		// all be separate parents.
		public static void ApplyTransformRecursive(
			Transform[] transforms,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			foreach (Transform transform in transforms)
			{
				ApplyTransformRecursive(transform, applyTranslation, applyRotation, applyScale);
			}
		}
		
		// Apply transform recursively for a parent and it's children.
		public static void ApplyTransformRecursive(
			Transform transform,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			Debug.Log("MeshApplyTransform:: Applying Transform on " + transform.name);
			ApplyTransform(transform, applyTranslation, applyRotation, applyScale);

			foreach (Transform child in transform)
			{
				ApplyTransformRecursive(child, applyTranslation, applyRotation, applyScale);
			}
		}

		// Apply individual transforms for multiple selected transforms.
		// It will apply the top-level transforms first, then update
		// children, but not siblings if those aren't selected.
		public static void ApplyTransform(
			Transform[] transforms,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			bool[] applied = new bool[transforms.Length];
			for (int i = 0; i < applied.Length; ++i) applied[i] = false;

			int applyCount = 0;

			while (applyCount != transforms.Length)
			{
				// Pass over list, finding unapplied transforms with no parents or applied parents.
				for	(int i = 0; i < transforms.Length; ++i)
				{
					// If this entry is unapplied...
					if (!applied[i])
					{
						bool canApply = true; // assume we can apply the transform.

						// Is the entry a child of an unapplied parent?
						for (int j = 0; j < transforms.Length; ++j)
						{
							if (i == j) continue; // ignore same entry.

							// If it's a child of unapplied parent,
							// we can't apply the transform to [i] in this pass.
							if (transforms[i].IsChildOf(transforms[j]) && !applied[j])
							{
								canApply = false;
								break;
							}
						}

						if (canApply)
						{
							ApplyTransform(transforms[i], applyTranslation, applyRotation, applyScale);
							applied[i] = true;
							Debug.Log("MeshApplyTransform:: Applied transform to " + transforms[i].name);
							++applyCount;
						}
					}
				}
			}
		} 
		
		// Apply an individual transform.
		public static void ApplyTransform(
			Transform transform,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			var meshFilter = transform.GetComponent<MeshFilter>();
			if (meshFilter != null)
			{
				Debug.Log("MeshApplyTransform:: Baking mesh for object (" + transform.name + ").");
				var originalMeshName = meshFilter.sharedMesh.name;

				var newMesh = ApplyTransform(
					transform,
					Instantiate(meshFilter.sharedMesh),
					applyTranslation, applyRotation, applyScale);

				Undo.RegisterCompleteObjectUndo(transform, "Apply Transform");

				meshFilter.sharedMesh = newMesh;

				if (!AssetDatabase.IsValidFolder("Assets/Baked Meshes"))
					AssetDatabase.CreateFolder("Assets", "Baked Meshes");

				var prefabPath = "";
				if (originalMeshName.StartsWith("BakedMesh"))
				{
					Debug.Log("MeshApplyTransform:: Replacing existing baked mesh (" + originalMeshName + ").");
					prefabPath = "Assets/Baked Meshes/" + originalMeshName + ".asset";
				}
				else
				{
					prefabPath = string.Format("Assets/Baked Meshes/BakedMesh_{0}_{1}_{2}.asset",
						transform.name, originalMeshName, (int)Mathf.Abs(newMesh.GetHashCode()));
				}
				
				AssetDatabase.CreateAsset(newMesh, prefabPath);
				AssetDatabase.SaveAssets();
			}

			// Even with no mesh filter, might still reset the transform
			// of a parent game object, i.e., reset either way.
			ResetTransform( transform,
					applyTranslation, applyRotation, applyScale );
		}

		// Apply a transform to a mesh. The transform needs to be
		// reset also after this application to keep the same shape.
		public static Mesh ApplyTransform(
			Transform transform,
			Mesh mesh,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			var verts = mesh.vertices;
			var norms = mesh.normals;
			
			// Handle vertices.
			for (int i = 0; i < verts.Length; ++i)
			{
				var nvert = verts[i];

				if (applyScale)
				{
					var scale = transform.localScale;
					nvert.x *= scale.x;
					nvert.y *= scale.y;
					nvert.z *= scale.z;
				}
			
				if (applyRotation)
				{
					nvert = transform.rotation * nvert;
				}

				if (applyTranslation)
				{
					nvert += transform.position;
				}

				verts[i] = nvert;
			}

			// Handle normals.
			for (int i = 0; i < verts.Length; ++i)
			{
				var nnorm = norms[i];

				if (applyRotation)
				{
					nnorm = transform.rotation * nnorm;
				}

				norms[i] = nnorm;
			}

			mesh.vertices = verts;
			mesh.normals = norms;

			mesh.RecalculateBounds();
			mesh.RecalculateTangents();

			return mesh;
		}

		// Reset the transform values, this should be executed after
		// applying the transform to the mesh data.
		public static Transform ResetTransform(
			Transform transform,
			bool applyTranslation,
			bool applyRotation,
			bool applyScale )
		{
			var scale = transform.localScale;
			var rotation = transform.localRotation;
			var translation = transform.position;

			// Update the children to keep their shape.
			foreach (Transform child in transform)
			{
				if (applyTranslation)
					child.Translate(transform.localPosition);

				if (applyRotation)
				{
					var worldPos = rotation * child.localPosition;
					child.localRotation = rotation * child.localRotation; 
					child.localPosition = worldPos;
				}

				if (applyScale)
				{
					var childScale = child.localScale;
					childScale.x *= scale.x;
					childScale.y *= scale.y;
					childScale.z *= scale.z;
					child.localScale = childScale;
					
					var childPosition = child.localPosition;
					childPosition.x *= scale.x;
					childPosition.y *= scale.y;
					childPosition.z *= scale.z;
					child.localPosition = childPosition;
				}

				// This makes the inspector update the position values.
				child.Translate(Vector3.zero);
				// Though for some reason the .position value is still screwed
				// for this frame though.
			}

			// Reset the transform.
			if (applyTranslation)
				transform.position = Vector3.zero;
			if (applyRotation)
				transform.rotation = Quaternion.identity;
			if (applyScale)
				transform.localScale = Vector3.one;

			return transform;
		}
	}
}