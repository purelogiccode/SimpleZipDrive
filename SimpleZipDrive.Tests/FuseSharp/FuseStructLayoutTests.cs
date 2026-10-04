using System.Runtime.InteropServices;
using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
/// Verifies the native struct and delegate layouts against the upstream libfuse/macFUSE
/// <c>fuse_operations</c> field order. A wrong order or size silently routes callbacks to the
/// wrong C function pointer, so these tests pin the ABI.
/// </summary>
public class FuseStructLayoutTests
{
    /// <summary>
    /// Verifies <c>fuse_args</c> starts with argc, argv and allocated.
    /// </summary>
    [Fact]
    public void FuseArgs_HasLibfuseLayout()
    {
        Assert.Equal(0, Marshal.OffsetOf<FuseArgs>(nameof(FuseArgs.Argc)).ToInt32());
        Assert.Equal(IntPtr.Size, Marshal.OffsetOf<FuseArgs>(nameof(FuseArgs.Argv)).ToInt32());
        Assert.Equal(IntPtr.Size * 2, Marshal.OffsetOf<FuseArgs>(nameof(FuseArgs.Allocated)).ToInt32());
    }

    /// <summary>
    /// Verifies the Linux operation table matches the upstream field order and truncation point.
    /// </summary>
    [Fact]
    public void FuseOperationsLinux_MatchesUpstreamFieldOrder()
    {
        Assert.Equal(29 * IntPtr.Size, Marshal.SizeOf<FuseOperationsLinux>());

        Assert.Equal(0, Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.GetAttr)).ToInt32());
        Assert.Equal(12 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.Open)).ToInt32());
        Assert.Equal(13 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.Read)).ToInt32());
        Assert.Equal(15 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.StatFs)).ToInt32());
        Assert.Equal(23 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.OpenDir)).ToInt32());
        Assert.Equal(24 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.ReadDir)).ToInt32());
        Assert.Equal(27 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.Init)).ToInt32());
        Assert.Equal(28 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsLinux>(nameof(FuseOperationsLinux.Destroy)).ToInt32());
    }

    /// <summary>
    /// Verifies the macFUSE operation table inserts the Darwin <c>setattr</c> right after
    /// <c>getattr</c> and keeps the remaining upstream order.
    /// </summary>
    [Fact]
    public void FuseOperationsMac_MatchesMacFuseFieldOrder()
    {
        Assert.Equal(30 * IntPtr.Size, Marshal.SizeOf<FuseOperationsMac>());

        Assert.Equal(0, Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.GetAttr)).ToInt32());
        Assert.Equal(IntPtr.Size, Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.SetAttr)).ToInt32());
        Assert.Equal(13 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.Open)).ToInt32());
        Assert.Equal(14 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.Read)).ToInt32());
        Assert.Equal(16 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.StatFs)).ToInt32());
        Assert.Equal(25 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.ReadDir)).ToInt32());
        Assert.Equal(28 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.Init)).ToInt32());
        Assert.Equal(29 * IntPtr.Size,
            Marshal.OffsetOf<FuseOperationsMac>(nameof(FuseOperationsMac.Destroy)).ToInt32());
    }

    /// <summary>
    /// Verifies the callback delegates use the C calling convention.
    /// </summary>
    [Theory]
    [InlineData(typeof(GetAttrDelegate))]
    [InlineData(typeof(SetAttrMacDelegate))]
    [InlineData(typeof(OpenDelegate))]
    [InlineData(typeof(ReadDelegate))]
    [InlineData(typeof(StatFsDelegate))]
    [InlineData(typeof(ReadDirDelegate))]
    [InlineData(typeof(FillDirDelegate))]
    [InlineData(typeof(InitDelegate))]
    public void CallbackDelegates_UseCdeclConvention(Type delegateType)
    {
        var attribute = Assert.Single(
            delegateType.GetCustomAttributes(typeof(UnmanagedFunctionPointerAttribute), false),
            item => item is UnmanagedFunctionPointerAttribute);

        Assert.Equal(CallingConvention.Cdecl, ((UnmanagedFunctionPointerAttribute)attribute).CallingConvention);
    }
}