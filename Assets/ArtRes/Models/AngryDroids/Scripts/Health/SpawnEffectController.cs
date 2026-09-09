using UnityEngine;
using System.Collections;
using GravityBox.AngryDroids;

//add visual representation of spawning a droid to the scene
public class SpawnEffectController : ScriptedAnimation
{
    public Gradient glowIntensity;
    public GameObject spawn;
    public Transform spawnPoint;
    public float spawnTime = 1f;

    private Material material;

    private Object _spawned;
    public Object spawned
    {
        get { return _spawned; }
        set { _spawned = value; }
    }

	protected override void OnAwake()
    {
        material = GetComponentInChildren<MeshRenderer>().material;
        material.color = glowIntensity.Evaluate(0); 
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer r in renderers) r.sharedMaterial = material;
	}

    protected override void OnDestroyGameObject()
    {
        Destroy(material);
    }

    protected override void UpdateAnimation(float normalizedTime)
    {
        material.color = glowIntensity.Evaluate(normalizedTime);

        if (normalizedTime > spawnTime)
        {
            Transform spawnAtPoint = spawnPoint == null ? transform : spawnPoint;
            if(_spawned == null)
                _spawned = Instantiate(spawn, spawnAtPoint.position, spawnAtPoint.rotation);
        }
	}
}
