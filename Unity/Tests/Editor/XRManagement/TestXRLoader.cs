using UnityEngine.XR.Management;

namespace BuildForge.Tests.XRManagement
{
    public sealed class TestXRLoader : XRLoader
    {
        public override T GetLoadedSubsystem<T>() => null;
    }
}
