using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace AlloyFramework
{
    /// <summary>
    /// Type-based finite state machine with a shared owner or data context.
    /// </summary>
    public class StateMachine<TContext> : IUpdateable, IDisposable
    {
        private readonly Dictionary<Type, State<TContext>> _states =
            new Dictionary<Type, State<TContext>>();

        private bool _isChangingState;

        public StateMachine(TContext context)
        {
            Context = context;
        }

        public event Action<State<TContext>, State<TContext>> StateChanged;

        public TContext Context { get; }

        public State<TContext> CurrentState { get; private set; }

        public Type CurrentStateType => CurrentState?.GetType();

        public int StateCount => _states.Count;

        public bool IsRunning => CurrentState != null;

        public bool IsDisposed { get; private set; }

        public TState AddState<TState>() where TState : State<TContext>, new()
        {
            return AddState(new TState());
        }

        public TState AddState<TState>(TState state) where TState : State<TContext>
        {
            ThrowIfDisposed();

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            Type stateType = state.GetType();
            if (_states.ContainsKey(stateType))
            {
                throw new InvalidOperationException($"State '{stateType.FullName}' is already registered.");
            }

            _states.Add(stateType, state);
            try
            {
                state.Initialize(this, Context);
                return state;
            }
            catch
            {
                _states.Remove(stateType);
                state.DisposeState();
                throw;
            }
        }

        public bool HasState<TState>() where TState : State<TContext>
        {
            ThrowIfDisposed();
            return _states.ContainsKey(typeof(TState));
        }

        public TState GetState<TState>() where TState : State<TContext>
        {
            ThrowIfDisposed();

            if (TryGetState(out TState state))
            {
                return state;
            }

            throw new KeyNotFoundException($"State '{typeof(TState).FullName}' is not registered.");
        }

        public bool TryGetState<TState>(out TState state) where TState : State<TContext>
        {
            ThrowIfDisposed();

            if (_states.TryGetValue(typeof(TState), out State<TContext> value))
            {
                state = (TState)value;
                return true;
            }

            state = null;
            return false;
        }

        public bool IsCurrentState<TState>() where TState : State<TContext>
        {
            ThrowIfDisposed();
            return CurrentState is TState;
        }

        public bool ChangeState<TState>() where TState : State<TContext>
        {
            ThrowIfDisposed();

            if (_isChangingState)
            {
                throw new InvalidOperationException(
                    "A state transition cannot be started from OnEnter or OnLeave.");
            }

            TState nextState = GetState<TState>();
            if (ReferenceEquals(CurrentState, nextState))
            {
                return false;
            }

            State<TContext> previousState = CurrentState;
            _isChangingState = true;
            try
            {
                previousState?.Leave();
                CurrentState = nextState;
                nextState.Enter();
            }
            finally
            {
                _isChangingState = false;
            }

            StateChanged?.Invoke(previousState, nextState);
            return true;
        }

        public bool Stop()
        {
            ThrowIfDisposed();

            if (_isChangingState)
            {
                throw new InvalidOperationException(
                    "The state machine cannot be stopped from OnEnter or OnLeave.");
            }

            if (CurrentState == null)
            {
                return false;
            }

            State<TContext> previousState = CurrentState;
            _isChangingState = true;
            try
            {
                previousState.Leave();
                CurrentState = null;
            }
            finally
            {
                _isChangingState = false;
            }

            StateChanged?.Invoke(previousState, null);
            return true;
        }

        public bool RemoveState<TState>() where TState : State<TContext>
        {
            ThrowIfDisposed();

            if (!_states.TryGetValue(typeof(TState), out State<TContext> state))
            {
                return false;
            }

            if (ReferenceEquals(CurrentState, state))
            {
                Stop();
            }

            _states.Remove(typeof(TState));
            state.DisposeState();
            return true;
        }

        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            ThrowIfDisposed();
            CurrentState?.Update(deltaTime, unscaledDeltaTime);
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Exception firstException = null;
            State<TContext> activeState = CurrentState;
            CurrentState = null;

            if (activeState != null)
            {
                try
                {
                    activeState.Leave();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }
            }

            foreach (State<TContext> state in _states.Values)
            {
                try
                {
                    state.DisposeState();
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                    {
                        firstException = exception;
                    }
                }
            }

            _states.Clear();
            StateChanged = null;
            IsDisposed = true;

            if (firstException != null)
            {
                ExceptionDispatchInfo.Capture(firstException).Throw();
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
