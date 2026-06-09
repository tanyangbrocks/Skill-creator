namespace SkillCreator.AbilitySystem.VM;

public enum OpCode
{
    Wait,          // 等待 N 秒（同步執行模式下跳過計時，Phase 3 搭配 SpellRunner 才有效）
    JumpIfFalse,   // If 積木：條件為 false 時跳到 else/end
    Jump,          // If 積木：跳過 else 分支
    SetVar,
    InvokeTotem,
    InvokeSpell,
    RepeatPush,    // RepeatN：把迭代次數推入 LoopCounter 堆疊
    RepeatStep,    // RepeatN：遞減計數，大於 0 則跳回循環起點；否則結束
}
