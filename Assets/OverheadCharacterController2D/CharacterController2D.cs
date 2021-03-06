﻿#define DEBUG_CC2D_RAYS
using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider2D), typeof(Rigidbody2D))]
public class CharacterController2D : MonoBehaviour {
	#region internal types

	struct CharacterRaycastOrigins {
		public Vector3 topLeft;
		public Vector3 bottomRight;
		public Vector3 bottomLeft;
	}

	public class CharacterCollisionState2D {
		public bool right;
		public bool left;
		public bool above;
		public bool below;

		public bool HasCollision () {
			return below || right || left || above;
		}


		public void Reset () {
			right = left = above = below = false;
		}


		public override string ToString () {
			return string.Format("[CharacterCollisionState2D] r: {0}, l: {1}, a: {2}, b: {3}",
				right, left, above, below
			);
		}
	}
	#endregion

	#region events, properties and fields
	public event Action<RaycastHit2D> OnControllerCollidedEvent;
	public event Action<Collider2D> OnTriggerEnterEvent;
	public event Action<Collider2D> OnTriggerStayEvent;
	public event Action<Collider2D> OnTriggerExitEvent;


	/// <summary>
	/// when true, one way platforms will be ignored when moving vertically for a single frame
	/// </summary>
	//public bool ignoreOneWayPlatformsThisFrame;

	[SerializeField]
	[Range(0.001f, 0.3f)]
	float _skinWidth = 0.02f;

	/// <summary>
	/// defines how far in from the edges of the collider rays are cast from. If cast with a 0 extent it will often result in ray hits that are
	/// not desired (for example a foot collider casting horizontally from directly on the surface can result in a hit)
	/// </summary>
	public float SkinWidth {
		get { return _skinWidth; }
		set {
			_skinWidth = value;
			RecalculateDistanceBetweenRays();
		}
	}


	/// <summary>
	/// mask with all layers that the player should interact with
	/// </summary>
	public LayerMask obstacleMask = 0;

	/// <summary>
	/// mask with all layers that trigger events should fire when intersected
	/// </summary>
	public LayerMask triggerMask = 0;

	[Range(2, 20)]
	public int totalHorizontalRays = 8;
	[Range(2, 20)]
	public int totalVerticalRays = 4;

	[HideInInspector][NonSerialized]
	public new Transform transform;
	[HideInInspector][NonSerialized]
	public BoxCollider2D boxCollider;
	[HideInInspector][NonSerialized]
	public Rigidbody2D rigidBody2D;

	[HideInInspector][NonSerialized]
	public CharacterCollisionState2D collisionState = new CharacterCollisionState2D ();
	[HideInInspector][NonSerialized]
	public Vector3 velocity;

	const float kSkinWidthFloatFudgeFactor = 0.001f;
	#endregion


	/// <summary>
	/// holder for our raycast origin corners (TR, TL, BR, BL)
	/// </summary>
	CharacterRaycastOrigins _raycastOrigins;

	/// <summary>
	/// stores our raycast hit during movement
	/// </summary>
	RaycastHit2D _raycastHit;
	RaycastHit2D _raycastProbeHit;

	/// <summary>
	/// stores any raycast hits that occur this frame. we have to store them in case we get a hit moving
	/// horizontally and vertically so that we can send the events after all collision state is set
	/// </summary>
	List<RaycastHit2D> _raycastHitsThisFrame = new List<RaycastHit2D> (2);

	// horizontal/vertical movement data
	float _verticalDistanceBetweenRays;
	float _horizontalDistanceBetweenRays;
	#region Monobehaviour

	void Awake () {
		// cache some components
		transform = GetComponent<Transform>();
		boxCollider = GetComponent<BoxCollider2D>();
		rigidBody2D = GetComponent<Rigidbody2D>();

		// here, we trigger our properties that have setters with bodies
		SkinWidth = _skinWidth;

		// we want to set our CC2D to ignore all collision layers except what is in our triggerMask
		for (var i = 0; i < 32; i++) {
			// see if our triggerMask contains this layer and if not ignore it
			if ((triggerMask.value & 1 << i) == 0)
				Physics2D.IgnoreLayerCollision(gameObject.layer, i);
		}
	}


	public void OnTriggerEnter2D (Collider2D col) {
		if (OnTriggerEnterEvent != null)
			OnTriggerEnterEvent(col);
	}


	public void OnTriggerStay2D (Collider2D col) {
		if (OnTriggerStayEvent != null)
			OnTriggerStayEvent(col);
	}


	public void OnTriggerExit2D (Collider2D col) {
		if (OnTriggerExitEvent != null)
			OnTriggerExitEvent(col);
	}
	#endregion


	[System.Diagnostics.Conditional("DEBUG_CC2D_RAYS")]
	void DrawRay (Vector3 start, Vector3 dir, Color color) {
		Debug.DrawRay(start, dir, color);
	}
	#region Public

	/// <summary>
	/// attempts to move the character to position + deltaMovement. Any colliders in the way will cause the movement to
	/// stop when run into.
	/// </summary>
	/// <param name="deltaMovement">Delta movement.</param>
	public void Move (Vector3 deltaMovement) {
		// clear our state
		collisionState.Reset();
		_raycastHitsThisFrame.Clear();

		PrimeRaycastOrigins();

		// now we check movement in the horizontal dir
		if (deltaMovement.x != 0f)
			MoveHorizontally(ref deltaMovement);

		// next, check movement in the vertical dir
		if (deltaMovement.y != 0f)
			MoveVertically(ref deltaMovement);

		// move then update our state
		deltaMovement.z = 0;
		transform.Translate(deltaMovement, Space.World);

		// only calculate velocity if we have a non-zero deltaTime
		if (Time.deltaTime > 0f)
			velocity = deltaMovement / Time.deltaTime;

		// send off the collision events if we have a listener
		if (OnControllerCollidedEvent != null) {
			for (var i = 0; i < _raycastHitsThisFrame.Count; i++)
				OnControllerCollidedEvent(_raycastHitsThisFrame [i]);
		}
	}

	/// <summary>
	/// this should be called anytime you have to modify the BoxCollider2D at runtime. It will recalculate the distance between the rays used for collision detection.
	/// It is also used in the skinWidth setter in case it is changed at runtime.
	/// </summary>
	public void RecalculateDistanceBetweenRays () {
		// figure out the distance between our rays in both directions
		// horizontal
		var colliderUseableHeight = boxCollider.size.y * Mathf.Abs(transform.localScale.y) - (2f * _skinWidth);
		_verticalDistanceBetweenRays = colliderUseableHeight / (totalHorizontalRays - 1);

		// vertical
		var colliderUseableWidth = boxCollider.size.x * Mathf.Abs(transform.localScale.x) - (2f * _skinWidth);
		_horizontalDistanceBetweenRays = colliderUseableWidth / (totalVerticalRays - 1);
	}
	#endregion


	#region Movement Methods
	/// <summary>
	/// resets the raycastOrigins to the current extents of the box collider inset by the skinWidth. It is inset
	/// to avoid casting a ray from a position directly touching another collider which results in wonky normal data.
	/// </summary>
	/// <param name="futurePosition">Future position.</param>
	/// <param name="deltaMovement">Delta movement.</param>
	void PrimeRaycastOrigins () {
		// our raycasts need to be fired from the bounds inset by the skinWidth
		var modifiedBounds = boxCollider.bounds;
		modifiedBounds.Expand(-2f * _skinWidth);

		_raycastOrigins.topLeft = new Vector2 (modifiedBounds.min.x, modifiedBounds.max.y);
		_raycastOrigins.bottomRight = new Vector2 (modifiedBounds.max.x, modifiedBounds.min.y);
		_raycastOrigins.bottomLeft = modifiedBounds.min;
	}

	/// <summary>
	/// we have to use a bit of trickery in this one. The rays must be cast from a small distance inside of our
	/// collider (skinWidth) to avoid zero distance rays which will get the wrong normal. Because of this small offset
	/// we have to increase the ray distance skinWidth then remember to remove skinWidth from deltaMovement before
	/// actually moving the player
	/// </summary>
	void MoveHorizontally (ref Vector3 deltaMovement) {
		var isGoingRight = deltaMovement.x > 0;
		var rayDistance = Mathf.Abs(deltaMovement.x) + _skinWidth;
		var rayDirection = isGoingRight ? Vector2.right : -Vector2.right;
		var initialRayOrigin = isGoingRight ? _raycastOrigins.bottomRight : _raycastOrigins.bottomLeft;

		for (var i = 0; i < totalHorizontalRays; i++) {
			var ray = new Vector2 (initialRayOrigin.x, initialRayOrigin.y + i * _verticalDistanceBetweenRays);

			DrawRay(ray, rayDirection * rayDistance, Color.red);

			_raycastHit = Physics2D.Raycast(ray, rayDirection, rayDistance, obstacleMask);
			if (_raycastHit) {
				// set our new deltaMovement and recalculate the rayDistance taking it into account
				deltaMovement.x = _raycastHit.point.x - ray.x;
				rayDistance = Mathf.Abs(deltaMovement.x);

				// remember to remove the skinWidth from our deltaMovement
				if (isGoingRight) {
					deltaMovement.x -= _skinWidth;
					collisionState.right = true;
				} else {
					deltaMovement.x += _skinWidth;
					collisionState.left = true;
				}

				_raycastHitsThisFrame.Add(_raycastHit);

				// we add a small fudge factor for the float operations here. if our rayDistance is smaller
				// than the width + fudge bail out because we have a direct impact
				if (rayDistance < _skinWidth + kSkinWidthFloatFudgeFactor)
					break;
			}
		}
	}

	void MoveVertically (ref Vector3 deltaMovement) {
		var isGoingUp = deltaMovement.y > 0;
		var rayDistance = Mathf.Abs(deltaMovement.y) + _skinWidth;
		var rayDirection = isGoingUp ? Vector2.up : -Vector2.up;
		var initialRayOrigin = isGoingUp ? _raycastOrigins.topLeft : _raycastOrigins.bottomLeft;

		// apply our horizontal deltaMovement here so that we do our raycast from the actual position we would be in if we had moved
		initialRayOrigin.x += deltaMovement.x;

		for (var i = 0; i < totalVerticalRays; i++) {
			var ray = new Vector2 (initialRayOrigin.x + i * _horizontalDistanceBetweenRays, initialRayOrigin.y);

			DrawRay(ray, rayDirection * rayDistance, Color.red);
			_raycastHit = Physics2D.Raycast(ray, rayDirection, rayDistance, obstacleMask);
			if (_raycastHit) {
				// set our new deltaMovement and recalculate the rayDistance taking it into account
				deltaMovement.y = _raycastHit.point.y - ray.y;
				rayDistance = Mathf.Abs(deltaMovement.y);

				// remember to remove the skinWidth from our deltaMovement
				if (isGoingUp) {
					deltaMovement.y -= _skinWidth;
					collisionState.above = true;
				} else {
					deltaMovement.y += _skinWidth;
					collisionState.below = true;
				}

				_raycastHitsThisFrame.Add(_raycastHit);

				// we add a small fudge factor for the float operations here. if our rayDistance is smaller
				// than the width + fudge bail out because we have a direct impact
				if (rayDistance < _skinWidth + kSkinWidthFloatFudgeFactor)
					break;
			}
		}
	}

	/// <summary>
	/// Probe ahead along a vector using the raycast anchors established for moving
	/// </summary>
	public bool Probe (Vector2 probeDistance) {
		return (_Probe(probeDistance).collider != null) ? true : false;
	}

	public bool Probe (Vector2 probeDistance, out RaycastHit2D hit) {
		hit = _Probe(probeDistance);
		return (hit.collider != null) ? true : false;
	}

	private RaycastHit2D _Probe (Vector2 probeDistance) {
		//FIXME don't do this... or maybe do who cares in this project for right now
		LayerMask collisionMask = obstacleMask;

		RaycastHit2D hit = new RaycastHit2D();
		Vector2 raycastAnchor;
		Vector2 anchor;
		Vector2 raycastDirection;
		PrimeRaycastOrigins();

		//Horizontal motion
		if (probeDistance.x != 0f) {
			if (probeDistance.x > 0f) {
				raycastAnchor = _raycastOrigins.bottomRight;
				raycastDirection = Vector2.right;
			} else {
				raycastAnchor = _raycastOrigins.bottomLeft;
				raycastDirection = Vector2.left;
			}
			for (var i = 0; i < totalHorizontalRays; i++) { 
				anchor = new Vector2 (raycastAnchor.x, raycastAnchor.y + i * _horizontalDistanceBetweenRays);
				DrawRay(anchor, raycastDirection, Color.blue);
				_raycastProbeHit = Physics2D.Raycast(anchor, raycastDirection, Mathf.Abs(probeDistance.x) + _skinWidth, collisionMask);
				if (_raycastProbeHit) {
					hit = _raycastProbeHit;
					//					return true;
				}
			}
		}

		//Vertical motion
		if (probeDistance.y != 0f) {
			if (probeDistance.y > 0f) {
				raycastAnchor = _raycastOrigins.topLeft;
				raycastDirection = Vector2.up;
			} else {
				raycastAnchor = _raycastOrigins.bottomLeft;
				raycastDirection = Vector2.down;
			}
			for (var i = 0; i < totalVerticalRays; i++) {
				anchor = new Vector2 (raycastAnchor.x + i * _horizontalDistanceBetweenRays, raycastAnchor.y);
				DrawRay(anchor, raycastDirection, Color.blue);
				_raycastProbeHit = Physics2D.Raycast(anchor, raycastDirection, Mathf.Abs(probeDistance.y) + _skinWidth, collisionMask);
				if (_raycastProbeHit) {
					hit = _raycastProbeHit;
				}
			}
		}

		return hit;
	}
	#endregion

}