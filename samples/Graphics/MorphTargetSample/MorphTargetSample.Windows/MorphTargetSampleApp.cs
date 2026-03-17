using System;
using Stride.Engine;
using Stride.Graphics;

AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.Error.WriteLine($"UNHANDLED: {e.ExceptionObject}");
    Console.Error.Flush();
};

using var game = new Game();
// Morph targets need Level_11_0 for constant buffer capacity (skinning + morph weights)
game.GraphicsDeviceManager.PreferredGraphicsProfile = new[] { GraphicsProfile.Level_11_0 };
game.GraphicsDeviceManager.ShaderProfile = GraphicsProfile.Level_11_0;
game.Run();
