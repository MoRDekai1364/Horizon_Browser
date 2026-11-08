using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Services;

/// <summary>
/// Multi-tier dGPU real-time blur fallback cascade for "Horizon Native Glass".
/// Invoked ONLY when primary DWM SetWindowCompositionAttribute(ACCENT_ENABLE_BLURBEHIND)
/// returns 0 or is blocked by OS policy:
///   Tier 2 (Fallback 1): DirectComposition (dcomp.dll) GaussianBlurEffect bound to an explicit
///                        high-performance dGPU ID3D11Device (DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE).
///   Tier 3 (Fallback 2): Zero-copy kernel shared-surface capture (DwmGetDxSharedSurface) of
///                        MainWindow's HWND opened on the dGPU via cached OpenSharedResource +
///                        ping-pong double-buffered non-blocking DMA (D3D11_MAP_FLAG_DO_NOT_WAIT) +
///                        GPU pixel-shader Gaussian blur.
///   Tier 4 (Safety Net): Solid #E6121212 obsidian dark grey.
/// </summary>
public static class NativeGlassFallbackService
{
    private const uint DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE = 2;
    private const uint D3D_DRIVER_TYPE_UNKNOWN = 0;
    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;
    private const uint D3D11_MAP_FLAG_DO_NOT_WAIT = 0x100000;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const double BleedPad = 48.0;

    private static readonly Guid IID_IDXGIFactory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    private static readonly Guid IID_IDXGIDevice   = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IDCompositionDesktopDevice = new("5f4633fe-1e08-4cb8-8c75-ce24333f5602");
    private static readonly Guid IID_IDCompositionDevice3       = new("0987cb06-f916-48bf-8d35-ce7641781bd9");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        uint driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("dcomp.dll", ExactSpelling = true)]
    private static extern int DCompositionCreateDevice2(
        IntPtr renderingDevice,
        ref Guid iid,
        out IntPtr dcompositionDevice);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DwmGetDxSharedSurface(
        IntPtr hwnd,
        out IntPtr phSurface,
        out long pAdapterLuid,
        out uint pFmtWindow,
        out uint pPresentFlags,
        out ulong pWin32kUpdateId);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", ExactSpelling = true)]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, nuint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_BOX
    {
        public uint left;
        public uint top;
        public uint front;
        public uint right;
        public uint bottom;
        public uint back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapterByGpuPreferenceDelegate(IntPtr thisPtr, uint adapter, uint gpuPreference, ref Guid riid, out IntPtr ppvAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenSharedResourceDelegate(IntPtr device, IntPtr hResource, ref Guid returnedInterface, out IntPtr ppResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr device, ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescDelegate(IntPtr texture, out D3D11_TEXTURE2D_DESC pDesc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopySubresourceRegionDelegate(
        IntPtr context, IntPtr pDstResource, uint dstSubresource, uint dstX, uint dstY, uint dstZ,
        IntPtr pSrcResource, uint srcSubresource, ref D3D11_BOX pSrcBox);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(IntPtr context, IntPtr pResource, uint subresource, uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(IntPtr context, IntPtr pResource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CommitDelegate(IntPtr dcompDevice);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateVisualDelegate(IntPtr dcompDevice, out IntPtr ppVisual);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTargetForHwndDelegate(IntPtr desktopDevice, IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr ppTarget);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetRootDelegate(IntPtr target, IntPtr visual);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateGaussianBlurEffectDelegate(IntPtr device3, out IntPtr ppEffect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetStandardDeviationFloatDelegate(IntPtr blurEffect, float amount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetBorderModeDelegate(IntPtr blurEffect, int mode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetEffectDelegate(IntPtr visual3, IntPtr effect);

    private static readonly object _deviceLock = new();
    private static IntPtr _dgpuDevice;
    private static IntPtr _dgpuContext;

    // Pre-cached vtable delegates for zero per-frame marshaling overhead
    private static OpenSharedResourceDelegate? _openSharedFn;
    private static CreateTexture2DDelegate? _createTexFn;
    private static CopySubresourceRegionDelegate? _copySubFn;
    private static MapDelegate? _mapFn;
    private static UnmapDelegate? _unmapFn;

    private static bool EnsureDgpuDevice(out IntPtr device, out IntPtr context)
    {
        lock (_deviceLock)
        {
            if (_dgpuDevice != IntPtr.Zero && _dgpuContext != IntPtr.Zero)
            {
                device = _dgpuDevice;
                context = _dgpuContext;
                return true;
            }

            IntPtr adapter = IntPtr.Zero;
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid factoryGuid = IID_IDXGIFactory6;
                if (CreateDXGIFactory1(ref factoryGuid, out factory) >= 0 && factory != IntPtr.Zero)
                {
                    IntPtr vtbl = Marshal.ReadIntPtr(factory);
                    IntPtr fnPtr = Marshal.ReadIntPtr(vtbl, 29 * IntPtr.Size);
                    var enumByPref = Marshal.GetDelegateForFunctionPointer<EnumAdapterByGpuPreferenceDelegate>(fnPtr);
                    Guid adapterGuid = IID_IDXGIAdapter1;
                    enumByPref(factory, 0, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, ref adapterGuid, out adapter);
                }

                int hr = adapter != IntPtr.Zero
                    ? D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION, out _dgpuDevice, out _, out _dgpuContext)
                    : D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION, out _dgpuDevice, out _, out _dgpuContext);

                if (hr < 0 || _dgpuDevice == IntPtr.Zero || _dgpuContext == IntPtr.Zero)
                {
                    _dgpuDevice = IntPtr.Zero;
                    _dgpuContext = IntPtr.Zero;
                    device = IntPtr.Zero;
                    context = IntPtr.Zero;
                    return false;
                }

                IntPtr devVtbl = Marshal.ReadIntPtr(_dgpuDevice);
                _createTexFn  = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(Marshal.ReadIntPtr(devVtbl, 5 * IntPtr.Size));
                _openSharedFn = Marshal.GetDelegateForFunctionPointer<OpenSharedResourceDelegate>(Marshal.ReadIntPtr(devVtbl, 28 * IntPtr.Size));

                IntPtr ctxVtbl = Marshal.ReadIntPtr(_dgpuContext);
                _mapFn     = Marshal.GetDelegateForFunctionPointer<MapDelegate>(Marshal.ReadIntPtr(ctxVtbl, 14 * IntPtr.Size));
                _unmapFn   = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(Marshal.ReadIntPtr(ctxVtbl, 15 * IntPtr.Size));
                _copySubFn = Marshal.GetDelegateForFunctionPointer<CopySubresourceRegionDelegate>(Marshal.ReadIntPtr(ctxVtbl, 46 * IntPtr.Size));

                device = _dgpuDevice;
                context = _dgpuContext;
                return true;
            }
            catch
            {
                device = IntPtr.Zero;
                context = IntPtr.Zero;
                return false;
            }
            finally
            {
                if (adapter != IntPtr.Zero) Marshal.Release(adapter);
                if (factory != IntPtr.Zero) Marshal.Release(factory);
            }
        }
    }

    public static void ApplyFallbackCascade(Window overlayWin, IntPtr overlayHwnd, Border rootBorder, Panel? glassBackdropHost = null)
    {
        // Tier 2 (Fallback 1): DirectComposition GaussianBlurEffect on explicit dGPU device
        if (TryEnableDCompDgpuBlur(overlayHwnd))
        {
            rootBorder.Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E));
            LogService.Debug("glass", () => "Activated Tier 2 Fallback: dGPU DirectComposition blur.");
            return;
        }

        // Tier 3 (Fallback 2): Zero-Copy DwmGetDxSharedSurface on MainWindow HWND + dGPU sub-rect blur
        if (TryEnableZeroCopySharedSurfaceBlur(overlayWin, rootBorder, glassBackdropHost))
        {
            LogService.Debug("glass", () => "Activated Tier 3 Fallback: dGPU DwmGetDxSharedSurface real-time blur.");
            return;
        }

        // Tier 4 (Final Safety Net): Solid obsidian dark grey
        rootBorder.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
    }

    private static bool TryEnableDCompDgpuBlur(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!EnsureDgpuDevice(out IntPtr d3dDevice, out _)) return false;

        IntPtr dxgiDevice = IntPtr.Zero;
        try
        {
            Guid dxgiGuid = IID_IDXGIDevice;
            if (Marshal.QueryInterface(d3dDevice, ref dxgiGuid, out dxgiDevice) < 0 || dxgiDevice == IntPtr.Zero)
                return false;

            Guid desktopGuid = IID_IDCompositionDesktopDevice;
            if (DCompositionCreateDevice2(dxgiDevice, ref desktopGuid, out IntPtr desktopDevice) < 0 || desktopDevice == IntPtr.Zero)
                return false;

            Guid dev3Guid = IID_IDCompositionDevice3;
            if (Marshal.QueryInterface(desktopDevice, ref dev3Guid, out IntPtr device3) < 0 || device3 == IntPtr.Zero)
                return false;

            IntPtr desktopVtbl = Marshal.ReadIntPtr(desktopDevice);
            var createTarget = Marshal.GetDelegateForFunctionPointer<CreateTargetForHwndDelegate>(Marshal.ReadIntPtr(desktopVtbl, 24 * IntPtr.Size));
            if (createTarget(desktopDevice, hwnd, false, out IntPtr target) < 0 || target == IntPtr.Zero)
                return false;

            var createVisual = Marshal.GetDelegateForFunctionPointer<CreateVisualDelegate>(Marshal.ReadIntPtr(desktopVtbl, 6 * IntPtr.Size));
            if (createVisual(desktopDevice, out IntPtr visual) < 0 || visual == IntPtr.Zero)
                return false;

            IntPtr dev3Vtbl = Marshal.ReadIntPtr(device3);
            var createBlur = Marshal.GetDelegateForFunctionPointer<CreateGaussianBlurEffectDelegate>(Marshal.ReadIntPtr(dev3Vtbl, 3 * IntPtr.Size));
            if (createBlur(device3, out IntPtr blurEffect) < 0 || blurEffect == IntPtr.Zero)
                return false;

            IntPtr blurVtbl = Marshal.ReadIntPtr(blurEffect);
            var setStdDev = Marshal.GetDelegateForFunctionPointer<SetStandardDeviationFloatDelegate>(Marshal.ReadIntPtr(blurVtbl, 4 * IntPtr.Size));
            var setBorderMode = Marshal.GetDelegateForFunctionPointer<SetBorderModeDelegate>(Marshal.ReadIntPtr(blurVtbl, 5 * IntPtr.Size));
            setStdDev(blurEffect, 12.0f);
            setBorderMode(blurEffect, 1); // D2D1_BORDER_MODE_HARD

            IntPtr visVtbl = Marshal.ReadIntPtr(visual);
            var setEffect = Marshal.GetDelegateForFunctionPointer<SetEffectDelegate>(Marshal.ReadIntPtr(visVtbl, 29 * IntPtr.Size));
            if (setEffect(visual, blurEffect) < 0)
                return false;

            IntPtr targetVtbl = Marshal.ReadIntPtr(target);
            var setRoot = Marshal.GetDelegateForFunctionPointer<SetRootDelegate>(Marshal.ReadIntPtr(targetVtbl, 3 * IntPtr.Size));
            if (setRoot(target, visual) < 0)
                return false;

            var commit = Marshal.GetDelegateForFunctionPointer<CommitDelegate>(Marshal.ReadIntPtr(desktopVtbl, 3 * IntPtr.Size));
            return commit(desktopDevice) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
        }
    }

    private static bool TryEnableZeroCopySharedSurfaceBlur(Window overlayWin, Border rootBorder, Panel? glassBackdropHost)
    {
        var mainWin = Application.Current?.MainWindow;
        if (mainWin == null || ReferenceEquals(mainWin, overlayWin)) return false;

        IntPtr mainHwnd = new WindowInteropHelper(mainWin).Handle;
        if (mainHwnd == IntPtr.Zero) return false;
        if (!EnsureDgpuDevice(out IntPtr d3dDevice, out IntPtr d3dContext)) return false;
        if (_openSharedFn == null || _createTexFn == null || _copySubFn == null || _mapFn == null || _unmapFn == null)
            return false;

        if (!DwmGetDxSharedSurface(mainHwnd, out IntPtr probeSurf, out _, out _, out _, out _) || probeSurf == IntPtr.Zero)
            return false;

        var hostPanel = glassBackdropHost ?? (rootBorder.Parent as Panel);
        if (hostPanel == null) return false;

        rootBorder.Background = Brushes.Transparent;

        var imageBrush = new ImageBrush { Stretch = Stretch.Fill };
        var blurWall = new Border
        {
            Margin = new Thickness(-BleedPad),
            Background = imageBrush,
            IsHitTestVisible = false,
            Effect = new BlurEffect
            {
                Radius = 36,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };
        var tintWall = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E)),
            IsHitTestVisible = false
        };

        var container = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        container.Children.Add(blurWall);
        container.Children.Add(tintWall);
        hostPanel.Children.Insert(0, container);

        ulong lastUpdateId = 0;
        Rect lastOverlayRect = Rect.Empty;
        double cachedDpiScale = VisualTreeHelper.GetDpi(overlayWin).DpiScaleX;
        if (cachedDpiScale <= 0) cachedDpiScale = 1.0;
        overlayWin.DpiChanged += (_, e) => { cachedDpiScale = e.NewDpi.DpiScaleX > 0 ? e.NewDpi.DpiScaleX : 1.0; };

        // Cached shared texture handle (only re-opened if MainWindow reallocates its swapchain)
        IntPtr cachedHSurface = IntPtr.Zero;
        IntPtr cachedSharedTex = IntPtr.Zero;
        D3D11_TEXTURE2D_DESC cachedSrcDesc = default;

        // Ping-pong double-buffered staging textures to eliminate GPU-CPU pipeline wait stalls
        IntPtr stagingTex0 = IntPtr.Zero;
        IntPtr stagingTex1 = IntPtr.Zero;
        uint stagingW = 0, stagingH = 0;
        int pingPongIdx = 0;
        bool isPrimed = false;
        WriteableBitmap? wb = null;

        void ReleaseResources()
        {
            if (cachedSharedTex != IntPtr.Zero) { Marshal.Release(cachedSharedTex); cachedSharedTex = IntPtr.Zero; }
            if (stagingTex0 != IntPtr.Zero) { Marshal.Release(stagingTex0); stagingTex0 = IntPtr.Zero; }
            if (stagingTex1 != IntPtr.Zero) { Marshal.Release(stagingTex1); stagingTex1 = IntPtr.Zero; }
            cachedHSurface = IntPtr.Zero;
        }

        void OnRender(object? sender, EventArgs e)
        {
            if (!overlayWin.IsVisible) return;
            if (!DwmGetDxSharedSurface(mainHwnd, out IntPtr hSurface, out _, out _, out _, out ulong updateId) || hSurface == IntPtr.Zero)
                return;

            double relLeftDip = overlayWin.Left - mainWin.Left - BleedPad;
            double relTopDip  = overlayWin.Top  - mainWin.Top  - BleedPad;
            double cropWDip   = (overlayWin.ActualWidth  > 0 ? overlayWin.ActualWidth  : overlayWin.Width)  + 2.0 * BleedPad;
            double cropHDip   = (overlayWin.ActualHeight > 0 ? overlayWin.ActualHeight : overlayWin.Height) + 2.0 * BleedPad;

            var curRect = new Rect(relLeftDip, relTopDip, cropWDip, cropHDip);
            if (updateId == lastUpdateId && curRect == lastOverlayRect && isPrimed)
                return;

            lastUpdateId = updateId;
            lastOverlayRect = curRect;

            try
            {
                if (hSurface != cachedHSurface || cachedSharedTex == IntPtr.Zero)
                {
                    if (cachedSharedTex != IntPtr.Zero) { Marshal.Release(cachedSharedTex); cachedSharedTex = IntPtr.Zero; }
                    Guid texGuid = IID_ID3D11Texture2D;
                    if (_openSharedFn(d3dDevice, hSurface, ref texGuid, out cachedSharedTex) < 0 || cachedSharedTex == IntPtr.Zero)
                        return;
                    cachedHSurface = hSurface;
                    IntPtr texVtbl = Marshal.ReadIntPtr(cachedSharedTex);
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(Marshal.ReadIntPtr(texVtbl, 10 * IntPtr.Size));
                    getDesc(cachedSharedTex, out cachedSrcDesc);
                }

                int x0 = Math.Clamp((int)(relLeftDip * cachedDpiScale), 0, (int)cachedSrcDesc.Width  - 1);
                int y0 = Math.Clamp((int)(relTopDip  * cachedDpiScale), 0, (int)cachedSrcDesc.Height - 1);
                int x1 = Math.Clamp((int)((relLeftDip + cropWDip) * cachedDpiScale), x0 + 1, (int)cachedSrcDesc.Width);
                int y1 = Math.Clamp((int)((relTopDip  + cropHDip) * cachedDpiScale), y0 + 1, (int)cachedSrcDesc.Height);
                uint w = (uint)(x1 - x0);
                uint h = (uint)(y1 - y0);
                if (w < 4 || h < 4) return;

                if (stagingTex0 == IntPtr.Zero || stagingTex1 == IntPtr.Zero || stagingW != w || stagingH != h)
                {
                    if (stagingTex0 != IntPtr.Zero) { Marshal.Release(stagingTex0); stagingTex0 = IntPtr.Zero; }
                    if (stagingTex1 != IntPtr.Zero) { Marshal.Release(stagingTex1); stagingTex1 = IntPtr.Zero; }
                    var desc = new D3D11_TEXTURE2D_DESC
                    {
                        Width = w,
                        Height = h,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                        SampleDescCount = 1,
                        SampleDescQuality = 0,
                        Usage = D3D11_USAGE_STAGING,
                        BindFlags = 0,
                        CpuAccessFlags = D3D11_CPU_ACCESS_READ,
                        MiscFlags = 0
                    };
                    if (_createTexFn(d3dDevice, ref desc, IntPtr.Zero, out stagingTex0) < 0 ||
                        _createTexFn(d3dDevice, ref desc, IntPtr.Zero, out stagingTex1) < 0)
                        return;
                    stagingW = w;
                    stagingH = h;
                    isPrimed = false;
                    wb = new WriteableBitmap((int)w, (int)h, 96, 96, PixelFormats.Bgra32, null);
                    imageBrush.ImageSource = wb;
                }

                IntPtr writeTex = (pingPongIdx & 1) == 0 ? stagingTex0 : stagingTex1;
                IntPtr readTex  = isPrimed ? ((pingPongIdx & 1) == 0 ? stagingTex1 : stagingTex0) : writeTex;
                pingPongIdx ^= 1;

                var box = new D3D11_BOX { left = (uint)x0, top = (uint)y0, front = 0, right = (uint)x1, bottom = (uint)y1, back = 1 };
                _copySubFn(d3dContext, writeTex, 0, 0, 0, 0, cachedSharedTex, 0, ref box);

                // Non-blocking map on primed frames so the UI thread never spin-waits on PCIe DMA
                uint mapFlags = isPrimed ? D3D11_MAP_FLAG_DO_NOT_WAIT : 0u;
                int mapHr = _mapFn(d3dContext, readTex, 0, D3D11_MAP_READ, mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped);
                if (mapHr >= 0 && wb != null && mapped.pData != IntPtr.Zero)
                {
                    try
                    {
                        wb.Lock();
                        try
                        {
                            IntPtr dst = wb.BackBuffer;
                            uint dstStride = (uint)wb.BackBufferStride;
                            uint rowBytes = w * 4;
                            if (mapped.RowPitch == dstStride)
                            {
                                CopyMemory(dst, mapped.pData, (nuint)(dstStride * h));
                            }
                            else
                            {
                                for (uint row = 0; row < h; row++)
                                {
                                    CopyMemory(
                                        dst + (nint)(row * dstStride),
                                        mapped.pData + (nint)(row * mapped.RowPitch),
                                        (nuint)rowBytes);
                                }
                            }
                            wb.AddDirtyRect(new Int32Rect(0, 0, (int)w, (int)h));
                        }
                        finally
                        {
                            wb.Unlock();
                        }
                        isPrimed = true;
                    }
                    finally
                    {
                        _unmapFn(d3dContext, readTex, 0);
                    }
                }
            }
            catch
            {
            }
        }

        CompositionTarget.Rendering += OnRender;
        overlayWin.Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRender;
            ReleaseResources();
        };

        return true;
    }
}
