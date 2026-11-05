using UnityEngine;

namespace PhonePointer
{
    // A synthetic screen ray, NOT a tracked physical position. No AR dependency.
    public static class AirMouseMath
    {
        public static Quaternion ToUnityAttitude(Quaternion q) => new Quaternion(q.x,q.y,-q.z,-q.w);
        public static void Pose(Quaternion reference, Quaternion current, Vector2 size, PoseSample sample)
        {
            var top=Quaternion.Inverse(reference)*current*Vector3.up;
            float yaw=Mathf.Atan2(top.x,top.y)*Mathf.Rad2Deg;
            float pitch=Mathf.Atan2(top.z,Mathf.Sqrt(top.x*top.x+top.y*top.y))*Mathf.Rad2Deg;
            // At 1x, +/-30 degrees horizontal and +/-20 degrees vertical reach the edges.
            var ray=new Vector3(yaw/60f*size.x,-pitch/40f*size.y,-1f);
            sample.position=Vector3.forward;
            sample.rotation=Quaternion.FromToRotation(Vector3.up,ray.normalized);
        }
    }
}
