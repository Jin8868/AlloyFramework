using UnityEngine;

namespace AlloyFramework
{
    public sealed class MonoFacade : MonoBehaviour
    {
        private void Awake()
        {
            if (!GameLoop.AttachFacade(this))
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            GameLoop.DispatchUpdate(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            GameLoop.DispatchLateUpdate(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            GameLoop.DispatchFixedUpdate(Time.fixedDeltaTime);
        }

        private void OnDestroy()
        {
            GameLoop.DetachFacade(this);
        }
    }
}
