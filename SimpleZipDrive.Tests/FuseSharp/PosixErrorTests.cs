using SimpleZipDrive.FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
/// Verifies the POSIX error numbers returned by the FUSE callbacks match the platform values.
/// </summary>
public class PosixErrorTests
{
    /// <summary>
    /// Verifies each constant equals the Linux/macOS errno value.
    /// </summary>
    [Fact]
    public void Constants_MatchPosixErrno()
    {
        Assert.Equal(2, PosixError.Enoent);
        Assert.Equal(5, PosixError.Eio);
        Assert.Equal(13, PosixError.Eacces);
        Assert.Equal(21, PosixError.Eisdir);
        Assert.Equal(22, PosixError.Einval);
        Assert.Equal(30, PosixError.Erofs);
    }
}