using System.Collections.Generic;
using JN.Client.Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace JN.Client.Scene
{
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("Drag Settings")]
        [SerializeField] private float dragSensitivity = 0.02f;  // 每像素拖拽对应的相机移动速度
        [SerializeField] private float smoothSpeed = 10f;        // 数值越大拖拽越灵敏

        [Header("Axis Movement Locks")]
        [SerializeField] private bool lockX = false;
        [SerializeField] private bool lockZ = false;

        [Header("Axis Bounds")]
        [SerializeField] private bool useXBounds = false;
        [SerializeField] private float minX = -20f;
        [SerializeField] private float maxX = 20f;

        /// <summary>
        /// 当前水平拖拽下限（Inspector 的 Min X）。
        /// </summary>
        public float MinX => minX;

        /// <summary>
        /// 运行时修改 Min X，并立刻把相机目标夹进新范围。
        /// </summary>
        public void SetMinX(float value)
        {
            minX = value;
            if (!useXBounds || lockX)
            {
                return;
            }

            _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);
            var clamped = transform.position;
            clamped.x = Mathf.Clamp(clamped.x, minX, maxX);
            transform.position = clamped;
        }

        [SerializeField] private bool useZBounds = false;
        [SerializeField] private float minZ = -20f;
        [SerializeField] private float maxZ = 20f;

        [Header("Input Filters")]
        [Tooltip("Layer ID that will block camera drag when raycast hits it (default 5 = UI)")]
        [SerializeField] private int blockLayerId = 5;

        [Header("Editor / PC Support")]
        [SerializeField] private bool allowMouseDragInEditor = true;

        [Header("Tap Detection")]
        [Tooltip("Max squared distance (in screen pixels^2) between down & up to still count as a tap")]
        [SerializeField] private float tapMaxMovementSqr = 25f;

        [Header("Tile Focus")]
        [SerializeField] private bool focusTileOnClick = true;
        [SerializeField] private float focusDistanceScale = 0.45f;
        [SerializeField] private float minFocusDistance = 10f;
        [SerializeField] private Vector3 focusWorldOffset = Vector3.zero;

        private Vector3 _targetPosition;
        private float _fixedHeightY;
        private bool _isDragging;
        private Vector2 _lastPointerPos;
        private Vector3 _lastFramePosition;
        private Vector2 _pointerDownPos;
        private bool _pointerDownValid;
        private bool _hasTileFocusDistance;
        private float _tileFocusDistance;
        private Vector3 _preTileFocusTargetPosition;
        private float _preTileFocusHeightY;

        public bool IsInTileFocusMode => _hasTileFocusDistance;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void FocusOnWorldPoint(Vector3 worldPoint)
        {
            if (!focusTileOnClick)
            {
                return;
            }

            var focusPoint = worldPoint + focusWorldOffset;
            var forward = transform.forward;
            if (!_hasTileFocusDistance)
            {
                _preTileFocusTargetPosition = _targetPosition;
                _preTileFocusHeightY = _fixedHeightY;
                var currentDistance = ResolveDistanceToFocusPoint(focusPoint, forward);
                _tileFocusDistance = Mathf.Max(minFocusDistance, currentDistance * focusDistanceScale);
                _hasTileFocusDistance = true;
            }

            var focusDistance = _tileFocusDistance;
            var cameraPosition = focusPoint - forward * focusDistance;

            SetFocusTargetPosition(cameraPosition);
        }

        public void ExitTileFocusMode()
        {
            if (!_hasTileFocusDistance)
            {
                return;
            }

            _hasTileFocusDistance = false;
            _tileFocusDistance = 0f;
            _fixedHeightY = _preTileFocusHeightY;
            SetTargetPosition(_preTileFocusTargetPosition);
        }

        private float ResolveDistanceToFocusPoint(Vector3 focusPoint, Vector3 forward)
        {
            if (Mathf.Abs(forward.y) > 0.0001f)
            {
                var distanceToFocusHeight = (focusPoint.y - transform.position.y) / forward.y;
                if (distanceToFocusHeight > 0f)
                {
                    return distanceToFocusHeight;
                }
            }

            return Mathf.Max(minFocusDistance, Vector3.Distance(transform.position, focusPoint));
        }

        private void SetFocusTargetPosition(Vector3 worldPos)
        {
            if (lockX) worldPos.x = _targetPosition.x;
            if (lockZ) worldPos.z = _targetPosition.z;

            _targetPosition = worldPos;
            _fixedHeightY = worldPos.y;

            if (useXBounds && !lockX)
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);

            if (useZBounds && !lockZ)
                _targetPosition.z = Mathf.Clamp(_targetPosition.z, minZ, maxZ);
        }

        /// <summary>
        /// 在场景启动后补齐依赖并刷新初始显示。
        /// </summary>
        private void Start()
        {
            _targetPosition = transform.position;
            _fixedHeightY = transform.position.y;

            if (useXBounds)
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);
            if (useZBounds)
                _targetPosition.z = Mathf.Clamp(_targetPosition.z, minZ, maxZ);

            _targetPosition.y = _fixedHeightY;
            transform.position = _targetPosition;
            _lastFramePosition = transform.position;
        }

        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            if (allowMouseDragInEditor)
                HandleMouseDrag();
            else
                HandleTouchDrag();
#else
            HandleTouchDrag();
#endif

            float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPosition, t);
        }

        /// <summary>
        /// 处理触摸拖拽。
        /// </summary>
        private void HandleTouchDrag()
        {
            if (Input.touchCount == 0)
            {
                _isDragging = false;
                _pointerDownValid = false;
                return;
            }

            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                BeginPointer(touch.position);
            }
            else if (touch.phase == TouchPhase.Moved && _isDragging)
            {
                Vector2 delta = touch.position - _lastPointerPos;
                _lastPointerPos = touch.position;

                if (_pointerDownValid && (touch.position - _pointerDownPos).sqrMagnitude > tapMaxMovementSqr)
                {
                    _pointerDownValid = false;
                }

                ApplyDrag(delta);
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                TryHandlePointerTap(touch.position, touch.fingerId);
                _isDragging = false;
                _pointerDownValid = false;
            }
        }

        /// <summary>
        /// 处理鼠标拖拽。
        /// </summary>
        private void HandleMouseDrag()
        {
            if (Input.GetMouseButtonDown(0))
            {
                BeginPointer(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && _isDragging)
            {
                Vector2 currentPos = Input.mousePosition;
                Vector2 delta = currentPos - _lastPointerPos;
                _lastPointerPos = currentPos;

                if (_pointerDownValid && (currentPos - _pointerDownPos).sqrMagnitude > tapMaxMovementSqr)
                {
                    _pointerDownValid = false;
                }

                ApplyDrag(delta);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                TryHandlePointerTap(Input.mousePosition);
                _isDragging = false;
                _pointerDownValid = false;
            }
        }

        private void BeginPointer(Vector2 screenPos)
        {
            _lastPointerPos = screenPos;
            _pointerDownPos = screenPos;

            if (IsPointerBlocked(screenPos))
            {
                _isDragging = false;
                _pointerDownValid = false;
                return;
            }

            _isDragging = true;
            _pointerDownValid = true;
        }

        /// <summary>
        /// 轻触抬起：走场景点击（桌位 / 员工 / 购买底板等）。
        /// </summary>
        private void TryHandlePointerTap(Vector2 screenPos, int pointerId = -1)
        {
            var isTap = (screenPos - _pointerDownPos).sqrMagnitude <= tapMaxMovementSqr;
            if (!isTap)
            {
                return;
            }

            // 二楼：点在购买价签上时 BeginPointer 会因「在 UI 上」把 _pointerDownValid 清掉，
            // 且无 TavernSceneManager 世界购买路径；这里补一次价签点击（TryBuy 内有短冷却防与 EventSystem 双触发）。
            if (SceneFlowCoordinator.IsOnTavernSecondFloor()
                && TryHandlePurchaseUiPointerClick(screenPos))
            {
                return;
            }

            if (!_pointerDownValid || IsPointerBlocked(screenPos))
            {
                return;
            }

            if (!Tile.TryHandlePointerClick(screenPos))
            {
                if (!TableArea.TryHandlePointerClick(screenPos))
                {
                    if (!TavernSceneManager.TryHandleStaffPointerClick(screenPos, pointerId))
                    {
                        TavernSceneManager.TryHandlePurchasePointerClick(screenPos);
                    }
                }
            }
        }

        /// <summary>
        /// 屏幕射线命中可购买价签时触发购买（二楼头顶价签主点击路径）。
        /// </summary>
        private static bool TryHandlePurchaseUiPointerClick(Vector2 screenPos)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPos
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            for (var index = 0; index < results.Count; index++)
            {
                var hitObject = results[index].gameObject;
                if (hitObject == null)
                {
                    continue;
                }

                var purchaseUi = hitObject.GetComponentInParent<TableAreaUI>();
                if (purchaseUi == null)
                {
                    continue;
                }

                purchaseUi.OnPointerClick(eventData);
                if (eventData.used)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 应用拖拽。
        /// </summary>
        /// <param name="screenDelta">参数值。</param>
        private void ApplyDrag(Vector2 screenDelta)
        {
            if (screenDelta.sqrMagnitude < 0.01f)
                return;

            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 move = (-screenDelta.x * right + -screenDelta.y * forward) * dragSensitivity;

            if (lockX) move.x = 0f;
            if (lockZ) move.z = 0f;

            _targetPosition += move;
            _targetPosition.y = _fixedHeightY;

            if (useXBounds && !lockX)
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);

            if (useZBounds && !lockZ)
                _targetPosition.z = Mathf.Clamp(_targetPosition.z, minZ, maxZ);
        }

        /// <summary>
        /// 在帧末同步跟随 界面 和场景表现位置。
        /// </summary>
        private void LateUpdate()
        {
            if (Vector3.SqrMagnitude(transform.position - _lastFramePosition) > 0.00001f)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                _lastFramePosition = transform.position;
            }
        }

        /// <summary>
        /// 立刻设置相机世界 X（仍受 min/max 约束），Y/Z 保持不变。
        /// </summary>
        public void SetWorldX(float x)
        {
            if (lockX)
            {
                return;
            }

            if (useXBounds)
            {
                x = Mathf.Clamp(x, minX, maxX);
            }

            var pos = transform.position;
            pos.x = x;
            transform.position = pos;
            _targetPosition = pos;
            _fixedHeightY = pos.y;
            _lastFramePosition = pos;
        }

        /// <summary>
        /// 设置目标位置。
        /// </summary>
        /// <param name="worldPos">参数值。</param>
        public void SetTargetPosition(Vector3 worldPos)
        {
            if (lockX) worldPos.x = _targetPosition.x;
            if (lockZ) worldPos.z = _targetPosition.z;

            worldPos.y = _fixedHeightY;
            _targetPosition = worldPos;

            if (useXBounds && !lockX)
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);

            if (useZBounds && !lockZ)
                _targetPosition.z = Mathf.Clamp(_targetPosition.z, minZ, maxZ);
        }

        /// <summary>
        /// 处理指针是否被阻挡相关逻辑。
        /// </summary>
        /// <param name="screenPos">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool IsPointerBlocked(Vector2 screenPos)
        {
            if (IsPointerOverUI(screenPos))
                return true;

            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(screenPos);
                int mask = 1 << blockLayerId;

                if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 处理指针是否悬停在界面相关逻辑。
        /// </summary>
        /// <param name="screenPosition">坐标。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            return results.Count > 0;
        }

        /// <summary>
        /// 处理点击。
        /// </summary>
        /// <param name="screenPos">参数值。</param>
        private void HandleTap(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
                return;

            Transform t = hit.transform;
            GameObject slotGO = null;

            while (t != null)
            {
                if (t.CompareTag("Slot"))
                {
                    slotGO = t.gameObject;
                    break;
                }
                t = t.parent;
            }

            if (slotGO == null)
                return;

            int slotIndex = -1;
            int slotLevel = 0;
            bool isBuilt = false;

            Debug.Log($"Tapped on slot: {slotIndex} lvl: {slotLevel}, isBuilt: {isBuilt}");

            if (slotIndex == -1)
            {
                Debug.LogWarning("[CameraController] HandleTap: no equipment slot uses this Slot GameObject as sceneParentPosition.");
                return;
            }
        }
    }
}
