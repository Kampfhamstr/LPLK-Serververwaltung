using Microsoft.Extensions.DependencyInjection;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;

namespace EnvironmentCheck;

[PluginMetadata(Id = "EnvironmentCheck", Version = "1.0.0", Name = "EnvironmentCheck", Author = "Server Administration", Description = "No description.")]
public partial class EnvironmentCheck : BasePlugin {
  public EnvironmentCheck(ISwiftlyCore core) : base(core)
  {
  }

  public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void UseSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void Load(bool hotReload) {
    
  }

  public override void Unload() {
  }
} 