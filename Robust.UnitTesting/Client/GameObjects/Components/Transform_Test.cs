﻿using System.IO;
using NUnit.Framework;
using Robust.Client.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.GameObjects.Components;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Robust.UnitTesting.Client.GameObjects.Components
{
    [TestFixture]
    [TestOf(typeof(TransformComponent))]
    class Transform_Test : RobustUnitTest
    {
        public override UnitTestProject Project => UnitTestProject.Client;

        private IClientEntityManager EntityManager;
        private IMapManager MapManager;

        const string PROTOTYPES = @"
- type: entity
  name: dummy
  id: dummy
  components:
  - type: Transform
";

        private IMap MapA;
        private IMapGrid GridA;
        private IMap MapB;
        private IMapGrid GridB;

        [OneTimeSetUp]
        public void Setup()
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.Register<TransformComponent>();
            factory.RegisterReference<TransformComponent, ITransformComponent>();

            EntityManager = IoCManager.Resolve<IClientEntityManager>();
            MapManager = IoCManager.Resolve<IMapManager>();

            var manager = IoCManager.Resolve<IPrototypeManager>();
            manager.LoadFromStream(new StringReader(PROTOTYPES));
            manager.Resync();

            // build the net dream
            MapA = MapManager.CreateMap();
            GridA = MapA.CreateGrid();

            MapB = MapManager.CreateMap();
            GridB = MapB.CreateGrid();

            //NOTE: The grids have not moved, so we can assert worldpos == localpos for the tests
        }

        /// <summary>
        ///     Make sure that component state locations are RELATIVE.
        /// </summary>
        [Test]
        public void ComponentStatePositionTest()
        {
            // Arrange
            var parent = EntityManager.SpawnEntity("dummy");
            var child = EntityManager.SpawnEntity("dummy");
            var parentTrans = parent.Transform;
            var childTrans = child.Transform;

            var compState = new TransformComponent.TransformComponentState(new Vector2(5, 5), GridB.Index, new Angle(0), EntityUid.Invalid);
            parentTrans.HandleComponentState(compState, null);

            compState = new TransformComponent.TransformComponentState(new Vector2(6, 6), GridB.Index, new Angle(0), EntityUid.Invalid);
            childTrans.HandleComponentState(compState, null);
            // World pos should be 6, 6 now.

            // Act
            var oldWpos = childTrans.WorldPosition;
            compState = new TransformComponent.TransformComponentState(new Vector2(1, 1), GridB.Index, new Angle(0), parent.Uid);
            childTrans.HandleComponentState(compState, null);
            var newWpos = childTrans.WorldPosition;

            // Assert
            Assert.That(newWpos, Is.EqualTo(oldWpos));
        }

        /// <summary>
        ///     Tests that world rotation is built properly
        /// </summary>
        [Test]
        public void WorldRotationTest()
        {
            // Arrange
            var node1 = EntityManager.SpawnEntity("dummy");
            var node2 = EntityManager.SpawnEntity("dummy");
            var node3 = EntityManager.SpawnEntity("dummy");

            node1.Name = "node1_dummy";
            node2.Name = "node2_dummy";
            node3.Name = "node3_dummy";

            var node1Trans = node1.Transform;
            var node2Trans = node2.Transform;
            var node3Trans = node3.Transform;

            var compState = new TransformComponent.TransformComponentState(new Vector2(6, 6), GridB.Index, Angle.FromDegrees(135), EntityUid.Invalid);
            node1Trans.HandleComponentState(compState, null);
            compState = new TransformComponent.TransformComponentState(new Vector2(1, 1), GridB.Index, Angle.FromDegrees(45), node1.Uid);
            node2Trans.HandleComponentState(compState, null);
            compState = new TransformComponent.TransformComponentState(new Vector2(0, 0), GridB.Index, Angle.FromDegrees(45), node2.Uid);
            node3Trans.HandleComponentState(compState, null);

            // Act
            var result = node3Trans.WorldRotation;

            // Assert (135 + 45 + 45 = 225)
            Assert.That(result, new ApproxEqualityConstraint(Angle.FromDegrees(225)));
        }
    }
}
