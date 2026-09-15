using UnityEngine;

namespace RedLightQwop.Editor
{
    /// <summary>
    /// Builds the low-poly ragdoll from primitives: eleven rigidbodies (flat siblings under one
    /// root, so no transform nesting fights the physics) joined by ConfigurableJoints with
    /// slerp drives. Dimensions are in meters with the feet resting at y = 0, facing +Z.
    /// </summary>
    public static class RagdollFactory
    {
        const float k_LegX = 0.12f;
        const float k_ArmX = 0.24f;

        public static GameObject Build(Material bodyMaterial, Material accentMaterial, PhysicsMaterial footMaterial)
        {
            var root = new GameObject("Ragdoll");
            var ragdoll = root.AddComponent<Ragdoll>();

            // --- Parts ---------------------------------------------------------------------
            var pelvis = Box(root, "Pelvis", new Vector3(0f, 1.05f, 0f), new Vector3(0.32f, 0.2f, 0.2f), 8f, accentMaterial, null);
            var torso = Box(root, "Torso", new Vector3(0f, 1.4f, 0f), new Vector3(0.36f, 0.5f, 0.22f), 10f, bodyMaterial, null);
            var head = Sphere(root, "Head", new Vector3(0f, 1.8f, 0f), 0.12f, 3f, bodyMaterial);

            var lUpperLeg = Capsule(root, "LeftUpperLeg", new Vector3(-k_LegX, 0.725f, 0f), 0.08f, 0.45f, 5f, accentMaterial, null);
            var lLowerLeg = Capsule(root, "LeftLowerLeg", new Vector3(-k_LegX, 0.25f, 0f), 0.08f, 0.5f, 3f, bodyMaterial, footMaterial);
            var rUpperLeg = Capsule(root, "RightUpperLeg", new Vector3(k_LegX, 0.725f, 0f), 0.08f, 0.45f, 5f, accentMaterial, null);
            var rLowerLeg = Capsule(root, "RightLowerLeg", new Vector3(k_LegX, 0.25f, 0f), 0.08f, 0.5f, 3f, bodyMaterial, footMaterial);
            AddFoot(lLowerLeg, accentMaterial, footMaterial);
            AddFoot(rLowerLeg, accentMaterial, footMaterial);

            var lUpperArm = Capsule(root, "LeftUpperArm", new Vector3(-k_ArmX, 1.45f, 0f), 0.05f, 0.3f, 2f, bodyMaterial, null);
            var lLowerArm = Capsule(root, "LeftLowerArm", new Vector3(-k_ArmX, 1.15f, 0f), 0.05f, 0.3f, 1.5f, bodyMaterial, null);
            var rUpperArm = Capsule(root, "RightUpperArm", new Vector3(k_ArmX, 1.45f, 0f), 0.05f, 0.3f, 2f, bodyMaterial, null);
            var rLowerArm = Capsule(root, "RightLowerArm", new Vector3(k_ArmX, 1.15f, 0f), 0.05f, 0.3f, 1.5f, bodyMaterial, null);

            // --- Joints (child, parent, anchor in child local space) -------------------------
            // X limits: negative swings a hanging limb forward, positive swings it back.
            ragdoll.Spine = Joint(torso, pelvis, new Vector3(0f, -0.25f, 0f), -30f, 30f, 20f, 20f, 1200f, 100f, "Spine");
            ragdoll.Neck = Joint(head, torso, new Vector3(0f, -0.15f, 0f), -30f, 30f, 30f, 30f, 200f, 20f, "Neck");

            // Y limit is generous so the hips can twist for steering.
            ragdoll.LeftHip = Joint(lUpperLeg, pelvis, new Vector3(0f, 0.225f, 0f), -100f, 45f, 40f, 15f, 1500f, 120f, "LeftHip");
            ragdoll.RightHip = Joint(rUpperLeg, pelvis, new Vector3(0f, 0.225f, 0f), -100f, 45f, 40f, 15f, 1500f, 120f, "RightHip");
            ragdoll.LeftKnee = Joint(lLowerLeg, lUpperLeg, new Vector3(0f, 0.25f, 0f), -3f, 130f, 3f, 3f, 1500f, 120f, "LeftKnee");
            ragdoll.RightKnee = Joint(rLowerLeg, rUpperLeg, new Vector3(0f, 0.25f, 0f), -3f, 130f, 3f, 3f, 1500f, 120f, "RightKnee");

            ragdoll.LeftShoulder = Joint(lUpperArm, torso, new Vector3(0f, 0.15f, 0f), -150f, 60f, 45f, 60f, 300f, 30f, "LeftShoulder");
            ragdoll.RightShoulder = Joint(rUpperArm, torso, new Vector3(0f, 0.15f, 0f), -150f, 60f, 45f, 60f, 300f, 30f, "RightShoulder");
            ragdoll.LeftElbow = Joint(lLowerArm, lUpperArm, new Vector3(0f, 0.15f, 0f), -140f, 3f, 3f, 3f, 300f, 30f, "LeftElbow");
            ragdoll.RightElbow = Joint(rLowerArm, rUpperArm, new Vector3(0f, 0.15f, 0f), -140f, 3f, 3f, 3f, 300f, 30f, "RightElbow");

            // --- Wire up the component ------------------------------------------------------
            ragdoll.Pelvis = pelvis.GetComponent<Rigidbody>();
            ragdoll.Torso = torso.GetComponent<Rigidbody>();
            ragdoll.Head = head.GetComponent<Rigidbody>();
            ragdoll.LeftUpperLeg = lUpperLeg.GetComponent<Rigidbody>();
            ragdoll.LeftLowerLeg = lLowerLeg.GetComponent<Rigidbody>();
            ragdoll.RightUpperLeg = rUpperLeg.GetComponent<Rigidbody>();
            ragdoll.RightLowerLeg = rLowerLeg.GetComponent<Rigidbody>();
            ragdoll.LeftUpperArm = lUpperArm.GetComponent<Rigidbody>();
            ragdoll.LeftLowerArm = lLowerArm.GetComponent<Rigidbody>();
            ragdoll.RightUpperArm = rUpperArm.GetComponent<Rigidbody>();
            ragdoll.RightLowerArm = rLowerArm.GetComponent<Rigidbody>();

            var controller = root.AddComponent<LimbController>();
            controller.Ragdoll = ragdoll;

            return root;
        }

        // ---------------------------------------------------------------------------------------

        static GameObject Part(GameObject root, string name, Vector3 position, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            return go;
        }

        static GameObject Visual(GameObject part, PrimitiveType type, Vector3 scale, Material material)
        {
            var visual = GameObject.CreatePrimitive(type);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(part.transform, false);
            visual.transform.localScale = scale;
            if (material != null) visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            return visual;
        }

        /// <summary>A flat foot rigidly attached to the lower leg (same rigidbody) so the doll
        /// stands on a surface rather than on two capsule tips.</summary>
        static void AddFoot(GameObject lowerLeg, Material material, PhysicsMaterial physicsMaterial)
        {
            var size = new Vector3(0.11f, 0.06f, 0.26f);
            var center = new Vector3(0f, -0.22f, 0.06f);
            var col = lowerLeg.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;
            col.sharedMaterial = physicsMaterial;

            var visual = Visual(lowerLeg, PrimitiveType.Cube, size, material);
            visual.name = "Foot";
            visual.transform.localPosition = center;
        }

        static GameObject Box(GameObject root, string name, Vector3 position, Vector3 size, float mass, Material material, PhysicsMaterial physicsMaterial)
        {
            var go = Part(root, name, position, mass);
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.sharedMaterial = physicsMaterial;
            Visual(go, PrimitiveType.Cube, size, material);
            return go;
        }

        static GameObject Sphere(GameObject root, string name, Vector3 position, float radius, float mass, Material material)
        {
            var go = Part(root, name, position, mass);
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            Visual(go, PrimitiveType.Sphere, Vector3.one * radius * 2f, material);
            return go;
        }

        static GameObject Capsule(GameObject root, string name, Vector3 position, float radius, float length, float mass, Material material, PhysicsMaterial physicsMaterial)
        {
            var go = Part(root, name, position, mass);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = radius;
            col.height = length;
            col.direction = 1; // Y
            col.sharedMaterial = physicsMaterial;
            // The primitive capsule mesh is 2 units tall and 1 unit wide.
            Visual(go, PrimitiveType.Capsule, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material);
            return go;
        }

        static Muscle Joint(GameObject child, GameObject parent, Vector3 anchor,
            float xLow, float xHigh, float yLimit, float zLimit, float spring, float damper, string label)
        {
            var joint = child.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.anchor = anchor;
            joint.autoConfigureConnectedAnchor = true;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            // xLow/xHigh are given in the same convention as Muscle.SetTargetEuler (negative X
            // swings a hanging limb forward). The joint's angular limits measure the twist with
            // the opposite sign, so swap and negate them here.
            joint.lowAngularXLimit = new SoftJointLimit { limit = -xHigh };
            joint.highAngularXLimit = new SoftJointLimit { limit = -xLow };
            joint.angularYLimit = new SoftJointLimit { limit = yLimit };
            joint.angularZLimit = new SoftJointLimit { limit = zLimit };

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = 100000f };

            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 5f;

            var muscle = child.AddComponent<Muscle>();
            muscle.Label = label;
            muscle.Spring = spring;
            muscle.Damper = damper;
            return muscle;
        }
    }
}
