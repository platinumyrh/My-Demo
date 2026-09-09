using UnityEngine;
using System.Collections.Generic;
using GravityBox.AngryDroids;
// TODO: Wait for this - will work in 3.3
//import System.Collections.Generic;

//[RequireComponent (typeof(Collider))]
public class PatrolRoute : MonoBehaviour
{
    public bool pingPong = false;
    public List<Vector3> points = new List<Vector3>(); 

//    private List<GameObject> activePatrollers = new List<GameObject>();

//    void OnTriggerEnter(Collider other)
//    {
//        if (activePatrollers.Contains(other.gameObject))
//        {
//            AI ai = other.gameObject.GetComponentInChildren<AI>();
////            if (ai)
////                ai.OnEnterInterestArea();
//        }
//    }

//    void OnTriggerExit(Collider other)
//    {
//        if (activePatrollers.Contains(other.gameObject))
//        {
//            AI ai = other.gameObject.GetComponentInChildren<AI>();
////            if (ai)
////                ai.OnExitInterestArea();
//        }
//    }

//    public void Register (GameObject go) 
//    {
//        activePatrollers.Add (go);
//    }

//    public void UnRegister (GameObject go) 
//    {
//        activePatrollers.Remove (go);
//    }

    public int GetClosestPatrolPoint (Vector3 pos)
    {
        if (points.Count == 0)
            return 0;

        float shortestDist = Mathf.Infinity;
        int shortestIndex = 0;

        for (int i = 0; i < points.Count; i++)
        {
            float dist = (GetPoint(i) - pos).sqrMagnitude;
            if (dist < shortestDist)
            {
                shortestDist = dist;
                shortestIndex = i;
            }
        }

        if (!pingPong || shortestIndex < points.Count - 1)
        {
            int nextIndex = (shortestIndex + 1) % points.Count;
            float angle = Vector3.Angle(
                GetPoint(nextIndex) - GetPoint(shortestIndex),
                GetPoint(shortestIndex) - pos
                );
            if (angle > 120)
                shortestIndex = nextIndex;
        }
        
        return shortestIndex;
    }

    public Vector3 GetPoint(int index)
    {
        return transform.TransformPoint(points[index]);
    }

    public Vector3[] GetPoints() 
    {
        Vector3[] result = points.ToArray();

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = transform.TransformPoint(result[i]);
        }

        return result;
    }
}