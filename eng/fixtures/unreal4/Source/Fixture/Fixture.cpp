//@if <4.15
#include "FixturePCH.h"
//@else
#include "Modules/ModuleManager.h"
//@endif
//@if <4.2
#include "FixtureGameMode.h"
#include "FixtureRunningState.h"
#include "Fixture.generated.inl"
//@endif

IMPLEMENT_PRIMARY_GAME_MODULE(FDefaultGameModuleImpl, Fixture, "Fixture");
