/* Independent reproduction of published Siemens C contracts, not SDK/NX.
 * Checks pointer levels, uint32 tags, string ownership and UF linked lists. */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

typedef uint32_t tag_t;
typedef struct { tag_t tag; int count; } *uf_list_p_t;
static int mode, invalid_free, string_free_calls, string_free_count;
static int compare_calls, topology_calls, list_delete_calls, parameter_calls;
static int face_body_calls, edge_body_calls;
static int current_calls, box_calls;
static tag_t last_feature, first_object, second_object, parameter_tag;
static tag_t reference_tag;
static tag_t current_part, box_tag;
static double last_facet, last_distance;
static void *allocated[16];
static int allocation_kind[16]; /* 1 plain, 2 names array, 3 name, 4 linked list */

static void *allocate(size_t size, int kind) {
    for (int i=0;i<16;++i) if (!allocated[i]) {
        allocated[i]=calloc(1,size); allocation_kind[i]=kind; return allocated[i];
    }
    abort();
}
static void release(void *value, int kind) {
    if (!value) return;
    for (int i=0;i<16;++i) if (allocated[i]==value) {
        if (allocation_kind[i]!=kind) { ++invalid_free; return; }
        free(value); allocated[i]=NULL; return;
    }
    ++invalid_free;
}
void UF_free(void *value) { release(value,1); }
void UF_free_string_array(int count, char **names) {
    ++string_free_calls; string_free_count=count;
    for (int i=0;i<count;++i) release(names[i],3);
    release(names,2);
}
void fixture_reset(int value) {
    for (int i=0;i<16;++i) if (allocated[i]) abort();
    mode=value; invalid_free=string_free_calls=string_free_count=0;
    compare_calls=topology_calls=list_delete_calls=parameter_calls=0;
    face_body_calls=edge_body_calls=0; reference_tag=0;
    current_calls=box_calls=0; current_part=box_tag=0;
    last_feature=first_object=second_object=parameter_tag=0;
    last_facet=last_distance=0;
}
int fixture_stat(int index) {
    int outstanding=0; for (int i=0;i<16;++i) outstanding+=allocated[i]!=NULL;
    const int values[]={outstanding,invalid_free,string_free_calls,string_free_count,
        compare_calls,topology_calls,list_delete_calls,parameter_calls,face_body_calls,edge_body_calls,current_calls,box_calls};
    return index>=0 && index<12 ? values[index] : -1;
}
tag_t fixture_tag(int index) {
    const tag_t values[]={last_feature,first_object,second_object,parameter_tag,reference_tag,current_part,box_tag};
    return index>=0 && index<7 ? values[index] : 0;
}
double fixture_tolerance(int index) { return index==0 ? last_facet : last_distance; }

static char *name_copy(const char *text) {
    char *p=allocate(strlen(text)+1,3); strcpy(p,text); return p;
}
int UF_MODL_ask_references_of_features(tag_t *features, int num_features,
        tag_t **parents, char ***names, int *count) {
    if (num_features!=1 || !features) return 90001;
    last_feature=features[0];
    if (mode==1) { *count=0; *parents=NULL; *names=NULL; return 0; }
    if (mode==2) return 777; /* No returned allocations or count. */
    if (mode==3) return 0; /* Successful but unwritten is not an empty list. */
    if (mode==4 || mode==5) {
        *count=mode==4 ? -2 : 2000000000;
        *parents=allocate(4,1); *names=allocate(sizeof(char *),1);
        return 0; /* Invalid count: no traversal of either allocation is safe. */
    }
    *count=3;
    *parents=mode==6 ? NULL : allocate(3*sizeof(tag_t),1);
    *names=mode==7 ? NULL : allocate(3*sizeof(char *),2);
    if (*parents) {
        (*parents)[0]=UINT32_C(0xf0000001); (*parents)[1]=29; (*parents)[2]=29;
        if (mode==8) (*parents)[1]=0;
    }
    if (*names) {
        (*names)[0]=name_copy("face");
        (*names)[1]=mode==9 ? NULL : name_copy(mode==11 ? "\xff" : "start");
        (*names)[2]=name_copy(mode==11 ? "\\xff" : "end");
    }
    return mode==10 ? 778 : 0; /* Error with valid partial output. */
}
int UF_MODL_compare(tag_t a, tag_t b, int relation, int *equivalent) {
    ++compare_calls; first_object=a; second_object=b;
    if (relation!=1) return 90002;
    if (mode==12) return 779;
    if (mode!=13) *equivalent=mode==14 ? 9 : mode==15 ? 0 : 1;
    return 0;
}
int UF_MODL_compare_topology(tag_t a, tag_t b, double facet, double distance, int *status) {
    ++topology_calls; first_object=a; second_object=b; last_facet=facet; last_distance=distance;
    if (mode==16) return 780;
    if (mode!=17) *status=mode==18 ? 99 : 5;
    return 0;
}
static int reference_body(tag_t reference, tag_t *body) {
    reference_tag=reference;
    if (mode==31) return 630003; /* Suppressed: no promise that UF can read it. */
    if (mode==32) return 0; /* Unwritten output must not become an absent owner. */
    *body=mode==33 ? 0 : UINT32_C(0xe0000042);
    return mode==34 ? 630003 : 0; /* Even a nonnull output cannot override an error. */
}
int UF_MODL_ask_face_body(tag_t face, tag_t *body) {
    ++face_body_calls; return reference_body(face,body);
}
int UF_MODL_ask_edge_body(tag_t edge, tag_t *body) {
    ++edge_body_calls; return reference_body(edge,body);
}
int UF_MODL_ask_current_feature(tag_t part, tag_t *feature) {
    ++current_calls; current_part=part;
    if (mode==35) return 791;
    if (mode!=36) *feature=mode==37 ? 0 : UINT32_C(0xe0000042);
    return 0;
}
int UF_MODL_ask_bounding_box(tag_t object, double box[6]) {
    ++box_calls; box_tag=object;
    if (mode==38) return 792;
    if (mode==39) return 0;
    const double values[6]={-12.5,2.25,3.0,4.5,5.75,6.0};
    memcpy(box,values,sizeof(values));
    if (mode==40) box[1]=INFINITY;
    if (mode==41) box[0]=100.0;
    return 0;
}
int UF_MODL_create_list(uf_list_p_t *list) {
    if (mode==19) { *list=NULL; return 0; }
    *list=allocate(sizeof(**list),4);
    return mode==20 ? 781 : 0;
}
int UF_MODL_put_list_item(uf_list_p_t list, tag_t value) {
    if (mode==21) return 782;
    list->tag=value; list->count=1; return 0;
}
int UF_MODL_ask_list_count(uf_list_p_t list, int *count) {
    if (mode==22) return 783;
    if (mode!=23) *count=mode==24 ? 2 : list->count;
    return 0;
}
int UF_MODL_ask_list_item(uf_list_p_t list, int index, tag_t *value) {
    if (index!=0) return 90003;
    if (mode==25) return 784;
    if (mode!=26) *value=mode==27 ? 29 : list->tag;
    return 0;
}
int UF_MODL_delete_object_parms(uf_list_p_t list) {
    ++parameter_calls; parameter_tag=list->tag;
    if (list->count!=1) return 90004;
    return mode==28 || mode==30 ? 785 : 0;
}
int UF_MODL_delete_list(uf_list_p_t *list) {
    ++list_delete_calls; release(*list,4); *list=NULL;
    return mode==29 || mode==30 ? 786 : 0;
}
