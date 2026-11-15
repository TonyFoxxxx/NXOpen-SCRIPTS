/* Independent UF custom-point ABI fixture, not a Siemens library. */
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

typedef struct {
    uint32_t point_tag;
    double point_acs[3];
    double upper_depth, lower_depth, depth;
} CustomPoint;

static void *allocated[8];
static int mode, frees, invalid_frees, deletes, appends, delete_error, append_error;
static uint32_t target;
static int channel, stored_count;
static CustomPoint stored[4];

static void *allocate(size_t size) {
    for (int i=0; i<8; ++i) if (!allocated[i]) {
        allocated[i]=calloc(1,size);
        return allocated[i];
    }
    abort();
}

void UF_free(void *p) {
    for (int i=0; i<8; ++i) if (allocated[i]==p && p) {
        free(p); allocated[i]=NULL; ++frees; return;
    }
    ++invalid_frees;
}

void fixture_reset(int m, int de, int ae) {
    for (int i=0; i<8; ++i) { free(allocated[i]); allocated[i]=NULL; }
    mode=m; delete_error=de; append_error=ae;
    frees=invalid_frees=deletes=appends=stored_count=0;
    target=0; channel=-1;
}

size_t fixture_layout(int field) {
    const size_t layout[]={sizeof(CustomPoint),offsetof(CustomPoint,point_tag),
        offsetof(CustomPoint,point_acs),offsetof(CustomPoint,upper_depth),
        offsetof(CustomPoint,lower_depth),offsetof(CustomPoint,depth)};
    return layout[field];
}

int fixture_stat(int field) {
    int outstanding=0;
    for (int i=0; i<8; ++i) outstanding += allocated[i]!=NULL;
    const int values[]={frees,invalid_frees,outstanding,deletes,appends,stored_count,channel};
    return values[field];
}

uint32_t fixture_target(void) { return target; }
void fixture_record(int index, CustomPoint *result) { *result=stored[index]; }

int UF_CAMGEOM_ask_custom_points(uint32_t tag, int kind, int *count, CustomPoint ***data) {
    target=tag; channel=kind;
    if (mode==8) { *count=0; *data=NULL; return 0; }
    *count=2;
    if (mode==1) { *data=NULL; return 0; }
    CustomPoint **list=allocate(2*sizeof(*list));
    *data=list;
    if (mode==6) { *count=-1; return 0; }
    if (mode==7) { *count=20001; return 0; }
    list[0]=allocate(sizeof(**list));
    list[0]->point_tag=0xf0000042u;
    list[0]->point_acs[0]=12.5;
    list[0]->point_acs[1]=-7.25;
    list[0]->point_acs[2]=3.0;
    list[0]->upper_depth=1.5;
    list[0]->lower_depth=-2.25;
    list[0]->depth=4.75;
    if (mode==4) list[1]=list[0];
    else if (mode!=2) {
        list[1]=allocate(sizeof(**list));
        list[1]->point_acs[0]=-100.0;
        list[1]->point_acs[2]=8.25;
    }
    if (mode==3) list[0]->point_acs[1]=NAN;
    return mode==5 ? 1345005 : 0;
}

int UF_CAMGEOM_delete_custom_points(uint32_t tag, int kind) {
    ++deletes; target=tag; channel=kind;
    if (delete_error) return delete_error;
    stored_count=0; return 0;
}

int UF_CAMGEOM_append_custom_points(uint32_t tag, int kind, int count, CustomPoint **points) {
    ++appends; target=tag; channel=kind;
    if (append_error) return append_error;
    if (count<1 || count>4 || !points) return 99991;
    for (int i=0; i<count; ++i) {
        if (!points[i]) return 99992;
        stored[i]=*points[i];
    }
    stored_count=count;
    return 0;
}
