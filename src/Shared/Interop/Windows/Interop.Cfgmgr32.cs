using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// No namespace: these files compile into several assemblies with different root namespaces,
// so a namespace-less internal type resolves in each without a using directive.
internal static partial class Interop
{
    /// <summary>Configuration Manager: device instance lookup, devnode tree walking, and devnode properties.</summary>
    [SupportedOSPlatform("windows")]
    internal static class Cfgmgr32
    {
        internal const int CR_SUCCESS = 0x00000000;

        internal const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
        internal const uint DN_HAS_PROBLEM = 0x00000400;

        internal const uint CM_DRP_DEVICEDESC = 0x00000001; // SPDRP_DEVICEDESC: device description / product string (REG_SZ)
        internal const uint CM_DRP_HARDWAREID = 0x00000002; // SPDRP_HARDWAREID: hardware IDs incl. REV_ (REG_MULTI_SZ)
        internal const uint CM_DRP_SERVICE = 0x00000005;    // SPDRP_SERVICE: driver service name e.g. "WinUSB" (REG_SZ)
        internal const uint CM_DRP_MFG = 0x0000000C;        // SPDRP_MFG: manufacturer string (REG_SZ)

        private const int MAX_DEVICE_ID_LEN = 400;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, uint bufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CM_Get_DevNode_Registry_PropertyW(
            uint dnDevInst, uint ulProperty, out uint pulRegDataType,
            char[] buffer, ref uint pulLength, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CM_Get_Device_Interface_List_SizeW(
            out uint pulLen, ref Guid interfaceClassGuid, string? pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CM_Get_Device_Interface_ListW(
            ref Guid interfaceClassGuid, string? pDeviceID, char[] buffer, uint bufferLen, uint ulFlags);

        /// <summary>The devnode's device instance ID (e.g. <c>USB\VID_2E8A&amp;PID_0003\serial</c>), or empty when it cannot be read.</summary>
        internal static string GetDeviceId(uint devNode)
        {
            char[] buffer = new char[MAX_DEVICE_ID_LEN];
            if (CM_Get_Device_IDW(devNode, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
                return "";
            int len = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, len < 0 ? buffer.Length : len);
        }

        /// <summary>A devnode's REG_SZ property, or empty when absent or unreadable.</summary>
        internal static string GetDevNodeProperty(uint devNode, uint property)
        {
            char[] buffer = new char[260];
            uint size = (uint)(buffer.Length * 2);
            return CM_Get_DevNode_Registry_PropertyW(devNode, property, out _, buffer, ref size, 0) == CR_SUCCESS && size > 2
                ? new string(buffer, 0, (int)(size / 2) - 1)
                : "";
        }

        /// <summary>A devnode's REG_MULTI_SZ property, split into its entries.</summary>
        internal static string[] GetDevNodeMultiSz(uint devNode, uint property)
        {
            char[] buffer = new char[1024];
            uint size = (uint)(buffer.Length * 2);
            return CM_Get_DevNode_Registry_PropertyW(devNode, property, out _, buffer, ref size, 0) != CR_SUCCESS || size < 2
                ? []
                : new string(buffer, 0, (int)(size / 2)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The device interface paths present for an interface class, or empty when it has none.</summary>
        internal static string[] GetDeviceInterfaceList(Guid interfaceClassGuid, string? deviceId = null)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint len, ref interfaceClassGuid, deviceId, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len <= 1)
                return [];
            char[] buffer = new char[len];
            return CM_Get_Device_Interface_ListW(ref interfaceClassGuid, deviceId, buffer, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS
                ? []
                : new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
