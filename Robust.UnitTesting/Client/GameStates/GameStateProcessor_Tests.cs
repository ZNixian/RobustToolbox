﻿using Moq;
using NUnit.Framework;
using Robust.Client.GameStates;
using Robust.Shared.GameStates;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.Timing;

namespace Robust.UnitTesting.Client.GameStates
{
    [TestFixture, Parallelizable, TestOf(typeof(GameStateProcessor))]
    class GameStateProcessor_Tests
    {
        [Test]
        public void FillBufferBlocksProcessing()
        {
            var timingMock = new Mock<IGameTiming>();
            timingMock.SetupProperty(p => p.CurTick);

            var timing = timingMock.Object;
            var processor = new GameStateProcessor(timing);

            processor.AddNewState(GameStateFactory(0, 1), 0);
            processor.AddNewState(GameStateFactory(1, 2), 0); // buffer is at 2/3, so processing should be blocked

            // calculate states for first tick
            timing.CurTick = new GameTick(3);
            var result = processor.ProcessTickStates(new GameTick(1), out _, out _);

            Assert.That(result, Is.False);
            Assert.That(timing.CurTick.Value, Is.EqualTo(1));
        }

        [Test]
        public void FillBufferAndCalculateFirstState()
        {
            var timingMock = new Mock<IGameTiming>();
            timingMock.SetupProperty(p => p.CurTick);

            var timing = timingMock.Object;
            var processor = new GameStateProcessor(timing);

            processor.AddNewState(GameStateFactory(0, 1), 0);
            processor.AddNewState(GameStateFactory(1, 2), 0);
            processor.AddNewState(GameStateFactory(2, 3), 0); // buffer is now full, otherwise cannot calculate states.

            // calculate states for first tick
            timing.CurTick = new GameTick(1);
            var result = processor.ProcessTickStates(new GameTick(1), out var curState, out var nextState);

            Assert.That(result, Is.True);
            Assert.That(curState, Is.Not.Null);
            Assert.That(curState.Extrapolated, Is.False);
            Assert.That(curState.ToSequence.Value, Is.EqualTo(1));
            Assert.That(nextState, Is.Null);
        }

        /// <summary>
        ///     When a full state is in the queue (fromSequence = 0), it will modify CurTick to the states' toSequence,
        ///     then return the state as curState.
        /// </summary>
        [Test]
        public void FullStateResyncsCurTick()
        {
            var timingMock = new Mock<IGameTiming>();
            timingMock.SetupProperty(p => p.CurTick);

            var timing = timingMock.Object;
            var processor = new GameStateProcessor(timing);

            processor.AddNewState(GameStateFactory(0, 1), 0);
            processor.AddNewState(GameStateFactory(1, 2), 0);
            processor.AddNewState(GameStateFactory(2, 3), 0); // buffer is now full, otherwise cannot calculate states.

            // calculate states for first tick
            timing.CurTick = new GameTick(3);
            processor.ProcessTickStates(timing.CurTick, out _, out _);

            Assert.That(timing.CurTick.Value, Is.EqualTo(1));
        }

        [Test]
        public void StatesReceivedPastCurTickAreDropped()
        {
            var (timing, processor) = SetupProcessorFactory();

            processor.Extrapolation = false;

            // a few moments later...
            timing.CurTick = new GameTick(5); // current clock is ahead of server
            processor.AddNewState(GameStateFactory(3, 4), 0); // received a late state
            var result = processor.ProcessTickStates(timing.CurTick, out _, out _);

            Assert.That(result, Is.False);
        }

        /// <summary>
        ///     The server fell behind the client, so the client clock is now ahead of the incoming states.
        ///     Without extrapolation, processing blocks.
        /// </summary>
        [Test]
        public void ServerLagsWithoutExtrapolation()
        {
            var (timing, processor) = SetupProcessorFactory();

            processor.Extrapolation = false;

            // a few moments later...
            timing.CurTick = new GameTick(5); // current clock is ahead of server
            var result = processor.ProcessTickStates(timing.CurTick, out _, out _);

            Assert.That(result, Is.False);
        }

        /// <summary>
        ///     When processing is blocked because the client is ahead of the server, reset CurTick to the last
        ///     received state.
        /// </summary>
        [Test]
        public void ServerLagsWithoutExtrapolationSetsCurTick()
        {
            var (timing, processor) = SetupProcessorFactory();

            processor.Extrapolation = false;

            // a few moments later...
            timing.CurTick = new GameTick(4); // current clock is ahead of server (server=1, client=5)
            var result = processor.ProcessTickStates(timing.CurTick, out _, out _);

            Assert.That(result, Is.False);
            Assert.That(timing.CurTick.Value, Is.EqualTo(1));
        }

        /// <summary>
        ///     The server fell behind the client, so the client clock is now ahead of the incoming states.
        ///     With extrapolation, processing returns a fake extrapolated state for the current tick.
        /// </summary>
        [Test]
        public void ServerLagsWithExtrapolation()
        {
            var (timing, processor) = SetupProcessorFactory();

            processor.Extrapolation = true;

            // a few moments later...
            timing.CurTick = new GameTick(5); // current clock is ahead of server

            var result = processor.ProcessTickStates(timing.CurTick, out var curState, out var nextState);

            Assert.That(result, Is.True);
            Assert.That(curState, Is.Not.Null);
            Assert.That(curState.Extrapolated, Is.True);
            Assert.That(curState.ToSequence.Value, Is.EqualTo(5));
            Assert.That(nextState, Is.Null);
        }

        /// <summary>
        ///     Creates a new empty GameState with the given to and from properties.
        /// </summary>
        private static GameState GameStateFactory(uint from, uint to)
        {
            return new GameState(new GameTick(from), new GameTick(to), null, null, null, null);
        }

        /// <summary>
        ///     Creates a new GameTiming and GameStateProcessor, fills the processor with enough states, and calculate the first tick.
        ///     CurTick = 1, states 1 - 3 are in the buffer.
        /// </summary>
        private static (IGameTiming timing, GameStateProcessor processor) SetupProcessorFactory()
        {
            var timingMock = new Mock<IGameTiming>();
            timingMock.SetupProperty(p => p.CurTick);
            timingMock.SetupProperty(p => p.TickTimingAdjustment);

            var timing = timingMock.Object;
            var processor = new GameStateProcessor(timing);

            processor.AddNewState(GameStateFactory(0, 1), 0);
            processor.AddNewState(GameStateFactory(1, 2), 0);
            processor.AddNewState(GameStateFactory(2, 3), 0); // buffer is now full, otherwise cannot calculate states.

            // calculate states for first tick
            timing.CurTick = new GameTick(1);
            processor.ProcessTickStates(timing.CurTick, out _, out _);
            
            return (timing, processor);
        }
    }
}
