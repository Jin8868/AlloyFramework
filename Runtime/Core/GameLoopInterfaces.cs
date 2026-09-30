namespace AlloyFramework
{
    public interface IUpdateable
    {
        void OnUpdate(float deltaTime, float unscaledDeltaTime);
    }

    public interface ILateUpdateable
    {
        void OnLateUpdate(float deltaTime, float unscaledDeltaTime);
    }

    public interface IFixedUpdateable
    {
        void OnFixedUpdate(float fixedDeltaTime);
    }
}
