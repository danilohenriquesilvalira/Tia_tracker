using System.Collections.Generic;

namespace TiaTracker.Core.BlockWriter
{
    // ── Tipos de blocos ──────────────────────────────────────────────────────
    public enum PlcBlockType { FC, FB }

    // ── Variável de interface ────────────────────────────────────────────────
    public class InterfaceVar
    {
        public string Name     { get; set; } = "Var1";
        public string DataType { get; set; } = "Bool";
        public string Default  { get; set; } = "";
        public string Comment  { get; set; } = "";
    }

    // ── Instrução FBD numa rede ──────────────────────────────────────────────
    public class InstructionDef
    {
        /// <summary>
        /// Tipo: "Move", "Coil", "SCoil", "RCoil",
        ///       "TON", "TOF", "TP", "TONR",
        ///       "CTU", "CTD",
        ///       "Add", "Sub", "Mul", "Div", "Mod",
        ///       "Neg", "Abs", "Inc", "Dec",
        ///       "Eq","Ne","Gt","Lt","Ge","Le",
        ///       "Convert", "Scale_X", "Normalize",
        ///       "AND", "OR"
        /// </summary>
        public string InstructType { get; set; } = "Move";

        /// <summary>Sinal de enable (vazio = Powerrail/sempre activo)</summary>
        public string EnableSignal { get; set; } = "";

        // ── Parâmetros genéricos (depende do tipo) ──────────────────────────
        /// <summary>
        /// IN (Move), IN1 (math/comparator), Operand (Inc/Dec),
        /// TimerInstance (TON/TOF/TP), CounterInstance (CTU/CTD),
        /// SrcType (Convert), Bobina (Coil)
        /// </summary>
        public string Param1 { get; set; } = "";

        /// <summary>IN2 (math/comparator), PT (timer), PV (counter)</summary>
        public string Param2 { get; set; } = "";

        /// <summary>DataType (Timer/Counter/Convert), R/LD (counter)</summary>
        public string Param3 { get; set; } = "";

        /// <summary>DestType (Convert)</summary>
        public string Param4 { get; set; } = "";

        // ── Saídas ──────────────────────────────────────────────────────────
        /// <summary>out1/Q/OUT: variável de destino principal</summary>
        public string OutVar1 { get; set; } = "";

        /// <summary>ET/CV (timer/counter): variável de destino secundária</summary>
        public string OutVar2 { get; set; } = "";

        // ── Rótulo ──────────────────────────────────────────────────────────
        public string Comment { get; set; } = "";
    }

    // ── Rede ────────────────────────────────────────────────────────────────
    public class NetworkDef
    {
        public string                Title        { get; set; } = "";
        public string                Comment      { get; set; } = "";
        public List<InstructionDef>  Instructions { get; set; } = new List<InstructionDef>();
    }

    // ── Bloco completo ───────────────────────────────────────────────────────
    public class BlockDefinition
    {
        public PlcBlockType        BlockType   { get; set; } = PlcBlockType.FC;
        public string              Name        { get; set; } = "FC_New";
        public int                 Number      { get; set; } = 100;
        public string              Description { get; set; } = "";
        public List<InterfaceVar>  Inputs      { get; set; } = new List<InterfaceVar>();
        public List<InterfaceVar>  Outputs     { get; set; } = new List<InterfaceVar>();
        public List<InterfaceVar>  InOuts      { get; set; } = new List<InterfaceVar>();
        public List<InterfaceVar>  Statics     { get; set; } = new List<InterfaceVar>(); // FB only
        public List<InterfaceVar>  Temps       { get; set; } = new List<InterfaceVar>();
        public List<NetworkDef>    Networks    { get; set; } = new List<NetworkDef>();
    }
}
