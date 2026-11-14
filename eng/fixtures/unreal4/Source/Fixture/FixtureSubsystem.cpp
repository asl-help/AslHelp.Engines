//@if >=4.22
#include "FixtureSubsystem.h"

void UFixtureSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
    Super::Initialize(Collection);

    SubsystemValue = 4242;
}
//@endif
