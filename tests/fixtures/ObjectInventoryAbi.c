/* UF read-only object cycling contract fixture, not Siemens implementation. */
#include <stdint.h>
#include <stddef.h>

static int mode, cycle_calls, type_calls;
static uint32_t last_part, last_previous, last_type;
static const uint32_t tags[] = {0xf0000011u, 0xe0000017u, 0xf0000055u, 0xd0000009u};

void fixture_reset(int value)
{
    mode=value;cycle_calls=type_calls=0;
    last_part=last_previous=last_type=0;
}
int fixture_stat(int index) { return index ? type_calls : cycle_calls; }
uint32_t fixture_tag(int index) { return index==0 ? last_part : index==1 ? last_previous : last_type; }

uint32_t UF_OBJ_cycle_all(uint32_t part, uint32_t previous)
{
    last_part=part;last_previous=previous;++cycle_calls;
    if(mode==1)return 0;
    if(previous==0)return tags[0];
    if(mode==2)return tags[0];
    for(size_t i=0;i<sizeof(tags)/sizeof(tags[0]);++i)
        if(previous==tags[i])return i+1<sizeof(tags)/sizeof(tags[0]) ? tags[i+1] : 0;
    return 0;
}

int UF_OBJ_ask_type_and_subtype(uint32_t tag, int *type, int *subtype)
{
    last_type=tag;++type_calls;
    if(mode==3)return 7408;
    if(mode!=4)*type=tag==tags[0] ? 1 : 113;
    if(mode!=5)*subtype=tag==tags[2] ? 6 : 3; /* Include non-collector containers. */
    return 0;
}
