using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;


//
//   <PackageReference Include="OpenTK" Version="4.8.2" />
//   < PackageReference Include = "AssimpNet" Version = "5.0.0" />
//    < !--Assimp native runtimes: pick what you need for your OS (keeping all is fine) -->
//    < PackageReference Include="AssimpNet.runtime.win-x64" Version="5.2.5" />
//    < PackageReference Include="AssimpNet.runtime.linux-x64" Version="5.2.5" />
//    < PackageReference Include="AssimpNet.runtime.osx-x64" Version="5.2.5" />
//    < PackageReference Include="StbImageSharp" Version="2.27.3" />
//
//

namespace GlbOpenTKDemo;

public static class Program
{
    public static void Main(string[] args)
    {
        var gws = GameWindowSettings.Default;
        var nws = new NativeWindowSettings 
        {
            ClientSize = new Vector2i(1280, 720),
            Title = "GLB + Assimp + OpenTK (net8)"
        };

        using var win = new Rendering.Viewer(gws, nws);
        win.Run();
    }
}
