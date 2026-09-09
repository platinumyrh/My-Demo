using UnityEngine;
using System.Collections;

namespace GravityBox.AngryDroids
{
    public class Sight
    {
        public static bool IsVisible(Bounds bounds, Transform transform, float FOV, float near, float far)
        {
            Vector3 forward = transform.forward;
            Vector3 position = transform.position;

            Vector3 point = bounds.center;
            float sm = (point - position).sqrMagnitude;
            if (sm > far * far || sm < near * near) return false;

            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            point.y = bounds.max.y;
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            point.y = bounds.min.y;
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            return false;
        }

        public static bool IsVisibleFull(Bounds bounds, Transform transform, float FOV, float near, float far)
        {
            Vector3 forward = transform.forward;
            Vector3 position = transform.position;


            Vector3 point = bounds.center;
            float sm = (point - position).sqrMagnitude;
            if (sm > far * far || sm < near * near) return false;

            //center
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            //frontal lower left
            point = bounds.min;
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;
            //back upper right
            point = bounds.max;
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            //frontal lower right
            point = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;
            //back lower left
            point = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;
            //back lower right
            point = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            //frontal upper left
            point = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;
            //back upper left
            point = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;
            //frontal upper right
            point = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
            if (Vector3.Angle(forward, (point - position)) < FOV * 0.5f)
                return true;

            return false;
        }
    }
}