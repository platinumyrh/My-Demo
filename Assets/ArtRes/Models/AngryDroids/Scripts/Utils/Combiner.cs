using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Combiner : MonoBehaviour {

    public bool combineToSkinned = true;

	void Awake () 
    {
        MeshFilter[] mfs = GetComponentsInChildren<MeshFilter>();
        Material mat = GetComponentInChildren<MeshRenderer>().sharedMaterial;
        if (combineToSkinned)
            CombineToSkinnedMesh(gameObject, mat, mfs);
        else
            CombineToMesh(gameObject, mat, mfs);
	}

    public static void CombineToMesh(GameObject target, Material material, MeshFilter[] filters)
    {
        if (material == null || filters == null || filters.Length <= 1) return;
        List<CombineInstance> combine = new List<CombineInstance>();

        int i = 0;
        while (i < filters.Length)
        {
            if (filters[i].sharedMesh != null)
            {
                CombineInstance ci = new CombineInstance();
                ci.mesh = filters[i].sharedMesh;
                ci.transform = target.transform.parent.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                combine.Add(ci);
                filters[i].GetComponent<MeshRenderer>().enabled = false;
            }
            i++;
        }

        GameObject combined = target.gameObject;
        
        MeshFilter mf = combined.AddComponent<MeshFilter>();
        mf.sharedMesh = new Mesh();
        mf.sharedMesh.name = target.transform.parent.gameObject.name;
        mf.sharedMesh.CombineMeshes(combine.ToArray(), true, true);
        mf.sharedMesh.RecalculateBounds();

        MeshRenderer mr = combined.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
    }

    public static void CombineToSkinnedMesh(GameObject target, Material material, MeshFilter[] filters)
    {
        if (material == null || filters == null || filters.Length <= 1) return;
        List<Transform> bones = new List<Transform>();
        List<BoneWeight> weights = new List<BoneWeight>();
        List<CombineInstance> combine = new List<CombineInstance>();

        int i = 0;
        while (i < filters.Length)
        {
            if (filters[i].sharedMesh != null)
            {
                CombineInstance ci = new CombineInstance();
                ci.mesh = filters[i].sharedMesh;
                ci.transform = filters[i].transform.localToWorldMatrix;
                combine.Add(ci);
                bones.Add(filters[i].transform);
                BoneWeight bw = new BoneWeight();
                bw.boneIndex0 = i;
                bw.weight0 = 1;
                for (int b = 0; b < filters[i].sharedMesh.vertexCount; b++)
                    weights.Add(bw);
                filters[i].GetComponent<MeshRenderer>().enabled = false;
            }
            i++;
        }

        GameObject combined = target.gameObject;
        List<Matrix4x4> bindposes = new List<Matrix4x4>();
        for (int b = 0; b < bones.Count; b++)
        {
            bindposes.Add(bones[b].worldToLocalMatrix);
        }

        SkinnedMeshRenderer mf = combined.AddComponent<SkinnedMeshRenderer>();
        mf.sharedMesh = new Mesh();
        mf.sharedMesh.name = target.transform.parent.gameObject.name;
        mf.sharedMesh.CombineMeshes(combine.ToArray(), true, true);
        mf.sharedMaterial = material;
        mf.bones = bones.ToArray();
        mf.sharedMesh.boneWeights = weights.ToArray();
        mf.sharedMesh.bindposes = bindposes.ToArray();
        mf.sharedMesh.RecalculateBounds();
        mf.rootBone = target.transform;
    }
}