namespace CVatGPT
{
    public class CVTargetHypothesis
    {
        public CVImageTarget target;
        public CVPose pose;

        public float Score => pose.confidence;

        public CVTargetHypothesis(CVImageTarget t)
        {
            target = t;
            pose = CVPose.Identity;
        }
    }
}
