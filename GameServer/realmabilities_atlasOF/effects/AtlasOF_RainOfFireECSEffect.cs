using DOL.GS.PacketHandler;

namespace DOL.GS.Effects
{
    public class AtlasOF_RainOfFireECSEffect : DamageAddECSEffect
    {
        public AtlasOF_RainOfFireECSEffect(ECSGameEffectInitParams initParams)
            : base(initParams)
        {
        }
        
        //public override ushort Icon { get { return 7125; } } //References spells.csv file
        public override string Name { get { return "Rain Of Fire"; } }
        public override bool HasPositiveEffect { get { return true; } }
    }
}
