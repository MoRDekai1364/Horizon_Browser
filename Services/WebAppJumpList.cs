using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Horizon.Stealth.Services;

internal static class WebAppJumpList
{
    private static readonly PropertyKey PkeyTitle = new PropertyKey
    {
        fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"),
        pid = 2
    };

    public static void Apply(string aumid, string exePath, string webAppId)
    {
        if (string.IsNullOrEmpty(aumid) || string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(webAppId)) return;
        try
        {
            var dl = (ICustomDestinationList)new CustomDestinationList();
            dl.SetAppID(aumid);
            var removedIid = typeof(IObjectArray).GUID;
            dl.BeginList(out _, ref removedIid, out _);

            var collection = (IObjectCollection)new EnumerableObjectCollection();
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exePath);
            link.SetArguments("--webapp-uninstall=" + webAppId);
            link.SetIconLocation(exePath, 0);

            var store = (IPropertyStore)link;
            var key = PkeyTitle;
            var pv = new PropVariant { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni("Uninstall") };
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref pv);
            }

            collection.AddObject(link);
            dl.AddUserTasks((IObjectArray)collection);
            dl.CommitList();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppJumpList.Apply");
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        int Commit();
    }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint cMaxSlots, ref Guid riid, out IObjectArray? ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class CustomDestinationList { }

    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, out object ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, out object ppv);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);
        void AddFromArray(IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("000214f9-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("00021401-0000-0000-c000-000000000046")]
    private class ShellLink { }
}