using Ecanakli.Janitor.DependencyInjection;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>The ProjectContext installer: list it under Mono Installers of the ProjectContext prefab.</summary>
    public sealed class ProjectInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            LifetimeInstaller.Install(Container);    // here the context lifetime is Lifetime.App
            Container.Bind<SceneFlow>().AsSingle();  // so its injected lifetime outlives every scene
        }
    }
}
