using System.Reflection;
using NUnit.Framework;
using UnityEngine.Scripting;

namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // Zenject calls the constructor of a bound type through reflection only, so managed stripping would remove it unless it is
    // preserved. This test pins the declaration; the stripping itself can only be checked in an IL2CPP player build.
    [TestFixture]
    public sealed class PreservedConstructorTests
    {
        private const BindingFlags AllInstanceConstructors = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void SceneContextLifetimeDisposer_TheOnlyConstructorIsPreserved()
        {
            var constructors = typeof(SceneContextLifetimeDisposer).GetConstructors(AllInstanceConstructors);

            Assert.That(constructors.Length, Is.EqualTo(1), "premise: Zenject picks the one constructor by reflection");
            Assert.That(constructors[0].IsDefined(typeof(PreserveAttribute), false), Is.True, "[Preserve] on the type keeps only a default constructor");
        }
    }
}
