using System;

namespace AlloyFramework
{
    /// <summary>
    /// Base class for a state owned by <see cref="StateMachine{TContext}"/>.
    /// A state instance can only belong to one state machine during its lifetime.
    /// </summary>
    public abstract class State<TContext>
    {
        public StateMachine<TContext> StateMachine { get; private set; }

        public TContext Context { get; private set; }

        public bool IsInitialized { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool IsActive =>
            StateMachine != null && ReferenceEquals(StateMachine.CurrentState, this);

        /// <summary>
        /// Called once when the state is registered.
        /// </summary>
        protected virtual void OnInit()
        {
        }

        /// <summary>
        /// Called whenever this state becomes active.
        /// </summary>
        protected virtual void OnEnter()
        {
        }

        /// <summary>
        /// Called while this state is active.
        /// </summary>
        protected virtual void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
        }

        /// <summary>
        /// Called whenever this state stops being active.
        /// </summary>
        protected virtual void OnLeave()
        {
        }

        /// <summary>
        /// Called once before the state is permanently discarded.
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        protected bool ChangeState<TState>() where TState : State<TContext>
        {
            EnsureAvailable();
            return StateMachine.ChangeState<TState>();
        }

        protected TState GetState<TState>() where TState : State<TContext>
        {
            EnsureAvailable();
            return StateMachine.GetState<TState>();
        }

        protected bool TryGetState<TState>(out TState state) where TState : State<TContext>
        {
            EnsureAvailable();
            return StateMachine.TryGetState(out state);
        }

        internal void Initialize(StateMachine<TContext> stateMachine, TContext context)
        {
            if (stateMachine == null)
            {
                throw new ArgumentNullException(nameof(stateMachine));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException("The state has already been initialized.");
            }

            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            StateMachine = stateMachine;
            Context = context;
            IsInitialized = true;
            OnInit();
        }

        internal void Enter()
        {
            EnsureAvailable();
            OnEnter();
        }

        internal void Update(float deltaTime, float unscaledDeltaTime)
        {
            EnsureAvailable();
            OnUpdate(deltaTime, unscaledDeltaTime);
        }

        internal void Leave()
        {
            EnsureAvailable();
            OnLeave();
        }

        internal void DisposeState()
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                if (IsInitialized)
                {
                    OnDispose();
                }
            }
            finally
            {
                IsDisposed = true;
                StateMachine = null;
                Context = default;
            }
        }

        private void EnsureAvailable()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (!IsInitialized || StateMachine == null)
            {
                throw new InvalidOperationException("The state has not been initialized.");
            }
        }
    }
}
