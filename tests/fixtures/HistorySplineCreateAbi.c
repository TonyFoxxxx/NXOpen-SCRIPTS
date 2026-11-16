/* Independent ABI fixture. Does not create geometry or load Siemens DLLs. */
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <math.h>
typedef struct {
    int num_poles, order, is_rational;
    double *knots, (*poles)[4], start_param, end_param;
} Spline;
typedef struct { int state_code, flag; double value[3]; } State;
_Static_assert(sizeof(Spline)==48 && offsetof(Spline,knots)==16 &&
               offsetof(Spline,poles)==24 && offsetof(Spline,start_param)==32, "spline ABI");
_Static_assert(sizeof(State)==32 && offsetof(State,value)==8, "state ABI");
static int mode, rational, calls, live, frees, bad_free;
static void *allocated;
void fixture_reset(int failure, int expected_rational) {
    mode=failure; rational=expected_rational; calls=live=frees=bad_free=0; allocated=NULL;
}
int fixture_stat(int field) {
    int values[]={calls,live,frees,bad_free};
    return field>=0 && field<4 ? values[field] : -1;
}
void UF_free(void *p) {
    if (!p || p!=allocated) {bad_free++;return;}
    free(p);allocated=NULL;live--;frees++;
}
int UF_CURVE_create_spline(Spline *s, uint32_t *tag, int *count, State **states) {
    calls++;
    if (!s || !tag || !count || !states || !s->knots || !s->poles ||
        *tag!=0 || *count!=-1 || *states!=NULL) return 900;
    double w=rational ? sqrt(0.5) : 1.;
    double expected[3][4]={{1.,0.,0.,1.},{w,w,0.,w},{0.,1.,0.,1.}};
    if (s->num_poles!=3 || s->order!=3 || s->is_rational!=rational ||
        fabs(s->start_param-.2)>1e-12 || fabs(s->end_param-.8)>1e-12) return 901;
    for (int j=0;j<6;j++) if (s->knots[j]!=(j<3 ? 0. : 1.)) return 902;
    for (int i=0;i<3;i++) for (int j=0;j<4;j++)
        if (fabs(s->poles[i][j]-expected[i][j])>1e-12) return 903;
    if (mode==1) return 711;
    if (mode!=2) *tag=UINT32_C(0xf1000029);
    if (mode!=3) *count=0;
    if (mode==4) *count=-2;
    if (mode==5) *count=1; /* Malformed positive count with no allocation. */
    if (mode==6 || mode==7 || mode==8 || mode==14) {
        State *p=calloc(2,sizeof(State));
        if (!p) return 904;
        allocated=*states=p;live++;
        p[0].state_code=101;p[0].flag=3;p[0].value[0]=2.;p[0].value[1]=5.;
        p[1].state_code=202;p[1].flag=4;p[1].value[2]=.125;
        *count=mode==8 ? 0 : mode==14 ? 20001 : 2;
    }
    if (mode==7) return 717;
    if (mode==9) s->knots[0]=-.01;
    if (mode==10) s->poles[1][0]+=.01;
    if (mode==11) s->is_rational=!s->is_rational;
    if (mode==12) s->knots=NULL;
    if (mode==13) s->end_param=.7;
    if (mode==15) s->poles=NULL;
    return 0;
}
