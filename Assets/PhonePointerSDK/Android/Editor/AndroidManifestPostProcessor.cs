using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace PhonePointer.Editor
{
    // Injects the VIBRATE permission into the Gradle project's merged AndroidManifest.xml after Unity
    // generates it. Doing it here (rather than shipping a custom manifest) avoids conflicting with the
    // manifest that the ARCore XR plugin merges in. VIBRATE is a normal permission, so no runtime prompt.
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifest = doc.DocumentElement;
            if (manifest == null) return;

            const string androidNs = "http://schemas.android.com/apk/res/android";
            const string permission = "android.permission.VIBRATE";
            bool changed = false;

            bool hasVibrate = false;
            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
            {
                var attr = node.Attributes?["android:name"];
                if (attr != null && attr.Value == permission) { hasVibrate = true; break; }
            }
            if (!hasVibrate)
            {
                var element = doc.CreateElement("uses-permission");
                var nameAttr = doc.CreateAttribute("android", "name", androidNs);
                nameAttr.Value = permission;
                element.Attributes.Append(nameAttr);
                manifest.AppendChild(element);
                changed = true;
            }

            // ARCore refuses to open the camera when the activity disables hardware acceleration.
            foreach (XmlElement activity in doc.GetElementsByTagName("activity"))
            {
                var accel = activity.GetAttributeNode("hardwareAccelerated", androidNs);
                if (accel == null)
                {
                    accel = doc.CreateAttribute("android", "hardwareAccelerated", androidNs);
                    activity.Attributes.Append(accel);
                }
                if (accel.Value == "true") continue;
                accel.Value = "true";
                changed = true;
            }

            if (changed) doc.Save(manifestPath);
        }
    }
}
