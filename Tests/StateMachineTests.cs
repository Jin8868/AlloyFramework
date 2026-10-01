using System.Collections.Generic;
using NUnit.Framework;

namespace AlloyFramework.Tests
{
    public sealed class StateMachineTests
    {
        [Test]
        public void TransitionAndDispose_RunLifecycleInOrder()
        {
            var calls = new List<string>();
            var context = new TestContext(calls);
            var stateMachine = new StateMachine<TestContext>(context);

            stateMachine.AddState<IdleState>();
            stateMachine.AddState<RunState>();

            Assert.That(stateMachine.ChangeState<IdleState>(), Is.True);
            Assert.That(stateMachine.ChangeState<IdleState>(), Is.False);
            stateMachine.OnUpdate(0.25f, 0.5f);
            Assert.That(stateMachine.ChangeState<RunState>(), Is.True);
            stateMachine.Dispose();

            Assert.That(calls, Is.EqualTo(new[]
            {
                "Idle.Init",
                "Run.Init",
                "Idle.Enter",
                "Idle.Update:0.25:0.5",
                "Idle.Leave",
                "Run.Enter",
                "Run.Leave",
                "Idle.Dispose",
                "Run.Dispose"
            }));
        }

        [Test]
        public void RemoveActiveState_LeavesAndDisposesIt()
        {
            var calls = new List<string>();
            var stateMachine = new StateMachine<TestContext>(new TestContext(calls));
            stateMachine.AddState<IdleState>();
            stateMachine.ChangeState<IdleState>();

            Assert.That(stateMachine.RemoveState<IdleState>(), Is.True);
            Assert.That(stateMachine.IsRunning, Is.False);
            Assert.That(stateMachine.HasState<IdleState>(), Is.False);
            Assert.That(calls, Is.EqualTo(new[]
            {
                "Idle.Init",
                "Idle.Enter",
                "Idle.Leave",
                "Idle.Dispose"
            }));
        }

        private sealed class TestContext
        {
            public TestContext(List<string> calls)
            {
                Calls = calls;
            }

            public List<string> Calls { get; }
        }

        private sealed class IdleState : State<TestContext>
        {
            protected override void OnInit()
            {
                Context.Calls.Add("Idle.Init");
            }

            protected override void OnEnter()
            {
                Context.Calls.Add("Idle.Enter");
            }

            protected override void OnUpdate(float deltaTime, float unscaledDeltaTime)
            {
                Context.Calls.Add($"Idle.Update:{deltaTime}:{unscaledDeltaTime}");
            }

            protected override void OnLeave()
            {
                Context.Calls.Add("Idle.Leave");
            }

            protected override void OnDispose()
            {
                Context.Calls.Add("Idle.Dispose");
            }
        }

        private sealed class RunState : State<TestContext>
        {
            protected override void OnInit()
            {
                Context.Calls.Add("Run.Init");
            }

            protected override void OnEnter()
            {
                Context.Calls.Add("Run.Enter");
            }

            protected override void OnLeave()
            {
                Context.Calls.Add("Run.Leave");
            }

            protected override void OnDispose()
            {
                Context.Calls.Add("Run.Dispose");
            }
        }
    }
}
