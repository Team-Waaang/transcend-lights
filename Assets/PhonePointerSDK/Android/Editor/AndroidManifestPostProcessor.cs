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

            // Skip if already present (e.g. added by another plugin).
            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
            {
                var attr = node.Attributes?["android:name"];
                if (attr != null && attr.Value == permission) return;
            }

            var element = doc.CreateElement("uses-permission");
            var nameAttr = doc.CreateAttribute("android", "name", androidNs);
            nameAttr.Value = permission;
            element.Attributes.Append(nameAttr);
            manifest.AppendChild(element);

            doc.Save(manifestPath);
        }
    }
}
